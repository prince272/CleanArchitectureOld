﻿namespace CleanArchitecture.Infrastructure.Extensions.EmailSender.MailKit
{
    public class MailKitEmailSenderOptions
    {
        public int SecureSocketId { get; set; }

        public bool UseServerCertificateValidation { get; set; }

        public string Hostname { get; set; } = null!;

        public int Port { get; set; }
    }
}
