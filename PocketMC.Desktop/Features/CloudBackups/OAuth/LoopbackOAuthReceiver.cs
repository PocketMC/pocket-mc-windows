using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Features.CloudBackups.OAuth;

public class LoopbackOAuthReceiver
{
    public async Task<(string? code, string? error)> ReceiveCodeAsync(
        string redirectUri,
        CancellationToken ct,
        string? expectedState = null)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            using var ctRegistration = ct.Register(() => listener.Stop());
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

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
}
