// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : LoggerFactoryImpl.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core.Abstractions {
    internal interface ILoggerFactory {
        ILogger CreateLogger(string name);
    }

    internal class LoggerFactoryImpl : ILoggerFactory {
        public ILogger CreateLogger(string name) {
            return new LoggerImpl(name);
        }
    }
}