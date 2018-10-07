// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : WebpackHttpModule.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core {
    using System;
    using System.Web;

    internal sealed class WebpackHttpModule : IHttpModule {
        public static WebpackOptions Options { get; set; }

        public WebpackHttpModule() {
            if (Options == null) {
                throw new InvalidOperationException($"{this.GetType()}: '{nameof(Options)}' property is null");
            }

            WebpackApplication.Activate();
        }

        public void Init(HttpApplication context) {
            WebpackApplication.Initialize(context);
        }

        public void Dispose() {
            // nothing
        }
    }
}