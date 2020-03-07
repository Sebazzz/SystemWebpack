// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : WebpackApplicationInstance.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;
    using Abstractions;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using NodeServices;
    using NodeServices.HostingModels;
    using NodeServices.Util;

    internal class WebpackApplication {
        private const string DefaultConfigFile = "webpack.config.js";
        
        private static readonly JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings {
            // Note that the aspnet-webpack JS code specifically expects options to be serialized with
            // PascalCase property names, so it's important to be explicit about this contract resolver
            ContractResolver = new DefaultContractResolver(),
            TypeNameHandling = TypeNameHandling.None
        };

        private static readonly WebpackApplication Instance = new WebpackApplication();
        private readonly object _syncRoot = new object();
        private readonly List<WebpackProxyRequestHandler> _proxyHandlers = new List<WebpackProxyRequestHandler>(2);

        private bool IsActivated => this._proxyHandlers.Count > 0;

        public static void Activate() {
            if (!Instance.IsActivated) {
                Instance.ActivateCore();
            }
        }

        private void ActivateCore() {
            lock (this._syncRoot) {
                Interlocked.MemoryBarrier();
                if (Instance.IsActivated) {
                    return;
                }

                WebpackOptions options = WebpackHttpModule.Options;

                if (options == null) {
                    throw new InvalidOperationException($"{typeof(WebpackHttpModule)}.{nameof(WebpackHttpModule.Options)} is null");
                }

                // Unlike other consumers of NodeServices, WebpackDevMiddleware doesn't share Node instances, nor does it
                // use your DI configuration. It's important for WebpackDevMiddleware to have its own private Node instance
                // because it must *not* restart when files change (if it did, you'd lose all the benefits of Webpack
                // middleware). And since this is a dev-time-only feature, it doesn't matter if the default transport isn't
                // as fast as some theoretical future alternative.
                NodeServicesOptions nodeServicesOptions = new NodeServicesOptions(WebAppAbstractions.ApplicationServices);
                nodeServicesOptions.WatchFileExtensions = new string[] { }; // Don't watch anything
                if (!string.IsNullOrEmpty(options.ProjectPath)) {
                    nodeServicesOptions.ProjectPath = options.ProjectPath;
                }

                if (options.EnvironmentVariables != null) {
                    foreach (var kvp in options.EnvironmentVariables) {
                        nodeServicesOptions.EnvironmentVariables[kvp.Key] = kvp.Value;
                    }
                }

                var nodeServices = NodeServicesFactory.CreateNodeServices(nodeServicesOptions);

                // Get a filename matching the middleware Node script
                var script = EmbeddedResourceReader.Read(typeof(WebpackApplication),
                    "/Content/Node/webpack-dev-middleware.js");
                var nodeScript = new StringAsTempFile(script, nodeServicesOptions.ApplicationStoppingToken); // Will be cleaned up on process exit

                // Ideally, this would be relative to the application's PathBase (so it could work in virtual directories)
                // but it's not clear that such information exists during application startup, as opposed to within the context
                // of a request.
                var hmrEndpoint = !string.IsNullOrEmpty(options.HotModuleReplacementEndpoint)
                    ? options.HotModuleReplacementEndpoint
                    : "/__webpack_hmr"; // Matches webpack's built-in default

                // Tell Node to start the server hosting webpack-dev-middleware
                var devServerOptions = new {
                    webpackConfigPath = Path.Combine(nodeServicesOptions.ProjectPath, options.ConfigFile ?? DefaultConfigFile),
                    suppliedOptions = options,
                    understandsMultiplePublicPaths = true,
                    hotModuleReplacementEndpointUrl = hmrEndpoint
                };
                var devServerInfo =
                    nodeServices.InvokeExportAsync<WebpackDevServerInfo>(nodeScript.FileName, "createWebpackDevServer",
                        JsonConvert.SerializeObject(devServerOptions, jsonSerializerSettings)).Result;

                // If we're talking to an older version of aspnet-webpack, it will return only a single PublicPath,
                // not an array of PublicPaths. Handle that scenario.
                if (devServerInfo.PublicPaths == null) {
                    devServerInfo.PublicPaths = new[] { devServerInfo.PublicPath };
                }

                // Proxy the corresponding requests through ASP.NET and into the Node listener
                // Anything under /<publicpath> (e.g., /dist) is proxied as a normal HTTP request with a typical timeout (100s is the default from HttpClient),
                // plus /__webpack_hmr is proxied with infinite timeout, because it's an EventSource (long-lived request).
                foreach (var publicPath in devServerInfo.PublicPaths) {
                    this.RegisterProxyToLocalWebpackDevMiddleware(publicPath + hmrEndpoint, devServerInfo.Port, Timeout.InfiniteTimeSpan);
                    this.RegisterProxyToLocalWebpackDevMiddleware(publicPath, devServerInfo.Port, TimeSpan.FromSeconds(100));
                }
            }
        }

        private void RegisterProxyToLocalWebpackDevMiddleware(string publicPath, int proxyToPort, TimeSpan requestTimeout) {
            // Note that this is hardcoded to make requests to "localhost" regardless of the hostname of the
            // server as far as the client is concerned. This is because ConditionalProxyMiddlewareOptions is
            // the one making the internal HTTP requests, and it's going to be to some port on this machine
            // because aspnet-webpack hosts the dev server there. We can't use the hostname that the client
            // sees, because that could be anything (e.g., some upstream load balancer) and we might not be
            // able to make outbound requests to it from here.
            // Also note that the webpack HMR service always uses HTTP, even if your app server uses HTTPS,
            // because the HMR service has no need for HTTPS (the client doesn't see it directly - all traffic
            // to it is proxied), and the HMR service couldn't use HTTPS anyway (in general it wouldn't have
            // the necessary certificate).
            var proxyOptions = new WebpackProxyRequestHandlerOptions(
                "http", "localhost", proxyToPort.ToString(), requestTimeout);
            this._proxyHandlers.Add(new WebpackProxyRequestHandler(publicPath, proxyOptions));
        }

        public static void Initialize(HttpApplication app) {
            EventHandlerTaskAsyncHelper helper = new EventHandlerTaskAsyncHelper(Instance.HandleRequest);
            app.AddOnBeginRequestAsync(helper.BeginEventHandler,helper.EndEventHandler);
        }

        private Task HandleRequest(object sender, EventArgs eventArgs) {
            HttpApplication app = (HttpApplication) sender;

            return this.HandleRequestCore(app);
        }

        private async Task HandleRequestCore(HttpApplication app) {
            foreach (WebpackProxyRequestHandler requestHandler in this._proxyHandlers) {
                bool hasHandledRequest = await requestHandler.Invoke(app.Context);

                if (hasHandledRequest) {
                    app.CompleteRequest();
                    return;
                }
            }
        }

#pragma warning disable CS0649
        class WebpackDevServerInfo {
            public int Port { get; set; }
            public string[] PublicPaths { get; set; }

            // For back-compatibility with older versions of aspnet-webpack, in the case where your webpack
            // configuration contains exactly one config entry. This will be removed soon.
            public string PublicPath { get; set; }
        }
    }
#pragma warning restore CS0649
}
