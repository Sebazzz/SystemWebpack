// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : WebpackProxyRequestHandler.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core {
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using System.Web;
    using Abstractions;

    internal sealed class WebpackProxyRequestHandler {
        private const int DefaultHttpBufferSize = 4096;

        private readonly HttpClient _httpClient;
        private readonly WebpackProxyRequestHandlerOptions _options;
        private readonly string _pathPrefix;
        private readonly bool _pathPrefixIsRoot;

        public WebpackProxyRequestHandler(
            string pathPrefix,
            WebpackProxyRequestHandlerOptions options) {
            if (!pathPrefix.StartsWith("/")) {
                pathPrefix = "/" + pathPrefix;
            }

            this._pathPrefix = pathPrefix;
            this._pathPrefixIsRoot = string.Equals(this._pathPrefix, "/", StringComparison.Ordinal);
            this._options = options;
            this._httpClient = new HttpClient(new HttpClientHandler());
            this._httpClient.Timeout = this._options.RequestTimeout;
        }

        public async Task<bool> Invoke(HttpContext context) {
            if (context.Request.Path.StartsWithSegments(this._pathPrefix) || this._pathPrefixIsRoot) {
                var didProxyRequest = await this.PerformProxyRequest(context).ConfigureAwait(false);
                if (didProxyRequest) {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> PerformProxyRequest(HttpContext context) {
            var requestMessage = new HttpRequestMessage();

            // Copy the request headers
            foreach (var key in context.Request.Headers.AllKeys) {
                if (!requestMessage.Headers.TryAddWithoutValidation(key, context.Request.Headers.GetValues(key) ?? new string[0])) {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(key, context.Request.Headers.GetValues(key) ?? new string[0]);
                }
            }

            requestMessage.Headers.Host = this._options.Host + ":" + this._options.Port;
            var uriString =
                $"{this._options.Scheme}://{this._options.Host}:{this._options.Port}{context.Request.Path}{context.Request.QueryString}";
            requestMessage.RequestUri = new Uri(uriString);
            requestMessage.Method = new HttpMethod(context.Request.HttpMethod);

            using (HttpResponseMessage responseMessage = await this._httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    context.Request.TimedOutToken).ConfigureAwait(false)) {
                if (responseMessage.StatusCode == HttpStatusCode.NotFound) {
                    // Let some other middleware handle this
                    return false;
                }

                // We don't want to buffer the output, otherwise hot middleware might never send a complete response
                context.Response.Buffer = false;
                context.Response.BufferOutput = false;

                // We can handle this
                context.Response.StatusCode = (int)responseMessage.StatusCode;
                foreach (var header in responseMessage.Headers) {
                    foreach (string headerValue in header.Value) {
                        context.Response.Headers.Add(header.Key, headerValue);
                    }
                }

                foreach (var header in responseMessage.Content.Headers) {
                    foreach (string headerValue in header.Value) {
                        context.Response.Headers.Add(header.Key, headerValue);
                    }
                }

                // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
                context.Response.Headers.Remove("transfer-encoding");

                // Pre-flush
#if NET46
                await context.Response.FlushAsync().ConfigureAwait(false);
#else
                context.Response.Flush();
#endif

                using (Stream responseStream = await responseMessage.Content.ReadAsStreamAsync()) {
                    try {
                        await responseStream.CopyToAsync(context.Response.OutputStream, DefaultHttpBufferSize, context.Request.TimedOutToken).ConfigureAwait(false);
                    } catch (OperationCanceledException) {
                        // The CopyToAsync task will be canceled if the client disconnects (e.g., user
                        // closes or refreshes the browser tab). Don't treat this as an error.
                    }
                }

                return true;
            }
        }
    }

    internal class WebpackProxyRequestHandlerOptions {
        public WebpackProxyRequestHandlerOptions(string scheme, string host, string port, TimeSpan requestTimeout) {
            this.Scheme = scheme;
            this.Host = host;
            this.Port = port;
            this.RequestTimeout = requestTimeout;
        }

        public string Scheme { get; }
        public string Host { get; }
        public string Port { get; }
        public TimeSpan RequestTimeout { get; }
    }
}