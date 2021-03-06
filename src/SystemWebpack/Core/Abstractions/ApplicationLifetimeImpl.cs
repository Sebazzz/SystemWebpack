﻿// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : ApplicationLifetimeImpl.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core.Abstractions {
    using System.Threading;
    using System.Web.Hosting;

    internal interface IApplicationLifetime {
        CancellationToken ApplicationStopping { get; }
    }

    internal sealed class ApplicationLifetimeImpl : IApplicationLifetime, IRegisteredObject {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public CancellationToken ApplicationStopping => this._cancellationTokenSource.Token;

        public void Stop(bool immediate) {
            this._cancellationTokenSource.Cancel();
        }
    }
} 