# HTTP Resilience & Fallback Loops

Be careful mixing global Polly resilience policies with custom application-level fallback logic.

## Pattern

When writing custom fallback loops across multiple backend proxy URLs (e.g., trying Proxy B if Proxy A fails), DO NOT attach global Polly Circuit Breaker policies (like `.AddStandardResilience()`) to the injected `HttpClient`. 

If the first proxy times out or returns a 5xx, the circuit breaker will open and immediately block the fallback attempt to the second proxy with a `BrokenCircuitException`. Instead, rely on explicit application logic inside the provider to discard the error and move to the next URL.

## When to Apply

Apply this when configuring new HTTP clients in `ServiceCollectionExtensions.cs` that are intended to be used with manual URL fallback loops.
