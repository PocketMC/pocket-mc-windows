using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Infrastructure.Backups.OAuth;

public class LoopbackOAuthReceiver
{
    public async Task<(string? code, string? error)> ReceiveCodeAsync(
        string redirectUri,
        CancellationToken ct,
        string? expectedState = null)
    {
        // Providers redirect to the registered URI without a trailing slash; HTTP.sys only routes
        // requests that fall under a registered prefix, so both spellings are registered here.
        string callback = redirectUri.TrimEnd('/');
        string callbackPath = new Uri(callback).AbsolutePath.TrimEnd('/');
        using var listener = new HttpListener();
        listener.Prefixes.Add(callback + "/");
        listener.Prefixes.Add(callback);
        listener.Start();

        try
        {
            using var ctRegistration = ct.Register(() => listener.Stop());
            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                // Browsers fetch favicons and speculative endpoints on the same port; ignore
                // anything that is not the callback so a stray request cannot consume the code.
                if (!MatchesCallbackPath(request.Url?.AbsolutePath, callbackPath))
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.ContentLength64 = 0;
                    response.Close();
                    continue;
                }

                string? code = request.QueryString.Get("code");
                string? error = request.QueryString.Get("error");
                string? state = request.QueryString.Get("state");

                if (!string.IsNullOrEmpty(expectedState) &&
                    !string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    code = null;
                    error = "invalid_state";
                }

                string responseString = string.IsNullOrEmpty(error)
                    ? "<html><body><h2>Authentication successful!</h2><p>You can close this tab and return to PocketMC.</p></body></html>"
                    : $"<html><body><h2>Authentication failed</h2><p>Error: {WebUtility.HtmlEncode(error)}</p><p>You can close this tab.</p></body></html>";

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                var responseOutput = response.OutputStream;
                await responseOutput.WriteAsync(buffer, 0, buffer.Length, ct);
                responseOutput.Close();

                return (code, error);
            }
        }
        catch (HttpListenerException) when (ct.IsCancellationRequested)
        {
            // Listener was stopped due to cancellation
            return (null, "cancelled");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    internal static bool MatchesCallbackPath(string? absolutePath, string callbackPath)
    {
        return !string.IsNullOrEmpty(absolutePath) &&
               string.Equals(absolutePath.TrimEnd('/'), callbackPath, StringComparison.Ordinal);
    }
}
