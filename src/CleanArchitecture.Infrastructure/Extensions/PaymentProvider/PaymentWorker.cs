﻿using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.Infrastructure.Extensions.PaymentProvider
{
    public class PaymentWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PaymentWorker> _logger;
        private string? previousExceptionMessage = null;

        public PaymentWorker(IServiceProvider serviceProvider,
            ILogger<PaymentWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            _logger.LogInformation($"{nameof(PaymentWorker)} has started.");
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            _logger.LogCritical($"{nameof(PaymentWorker)} has stopped.");
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{nameof(PaymentWorker)} is running.");

            while (!cancellationToken.IsCancellationRequested)
            {
                using var scope = _serviceProvider.CreateScope();
                var appDbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var paymentProvider = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();

                try
                {
                    var payemnts = await appDbContext.Set<Payment>().Where(_ => _.Status == PaymentStatus.Pending || _.Status == PaymentStatus.Processing).ToListAsync(cancellationToken);

                    foreach (var payemnt in payemnts)
                    {
                        try
                        {
                            await paymentProvider.VerifyAsync(payemnt, cancellationToken);
                        }
                        catch (Exception exception)
                        {
                            if (previousExceptionMessage == null || exception.Message != previousExceptionMessage)
                            {
                                _logger.LogError(exception, $"{nameof(PaymentWorker)} threw an exception.");
                            }

                            previousExceptionMessage = exception.Message;
                        }
                    }

                    previousExceptionMessage = null;
                }
                catch (Exception exception)
                {
                    if (previousExceptionMessage == null || exception.Message != previousExceptionMessage)
                    {
                        _logger.LogError(exception, $"{nameof(PaymentWorker)} threw an exception.");
                    }

                    previousExceptionMessage = exception.Message;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

        }
    }
}