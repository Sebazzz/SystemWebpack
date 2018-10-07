// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : WebpackSupport.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack {
    using System;
    using System.Web;
    using Core;

    public static class WebpackSupport {
        public static void Enable(string buildPath) => Enable(new WebpackOptions { BuildPath = buildPath });

        public static void Enable(WebpackOptions options) {
            if (options == null) throw new ArgumentNullException(nameof(options));

            options.Validate();

            // Register HTTP module with IIS
            WebpackHttpModule.Options = options;
            HttpApplication.RegisterModule(typeof(WebpackHttpModule));
        }
    }
}