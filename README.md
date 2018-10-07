# SystemWebpack - Webpack dev server support for System.Web (old ASP.NET)
It is very nice that we have ASP.NET Core, and got nice toys to play with like built-in support for the Webpack dev server. But what about System.Web? So many projects are still built on the old ASP.NET framework, where we don't have Webpack dev server support.

This library aims to fix that. This library provides support for the good parts of Webpack in System.Web projects. This library is heavily inspired by [Microsoft.AspNetCore.JavascriptServices](https://github.com/aspnet/JavaScriptServices) and in fact uses much code from that project.

## Features

Support for:
- Webpack dev middleware
- Hot module replacement

Used for development purposes only.

## Building the project
To build the project ensure you have:

- .NET Framework 4.5.2 or higher installed
- Visual Studio with Web Development tools
- Powershell 4 or higher

To build the project simply run:

    build
