﻿// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : SystemWebpackConfiguration.cs
//  Project         : SystemWebpackTestApp
// ******************************************************************************

using System.Web;

[assembly: PreApplicationStartMethod(typeof(SystemWebpackTestApp.SystemWebpackConfiguration), nameof(SystemWebpackTestApp.SystemWebpackConfiguration.Configure))]

namespace SystemWebpackTestApp {
    using SystemWebpack;

    public static class SystemWebpackConfiguration {
        public static void Configure() {
            WebpackSupport.Enable(new WebpackOptions {
                BuildPath = "/build",
                HotModuleReplacement =   true
            });
        }
    }
}