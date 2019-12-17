﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace CHI.Application
{
    public class LicenseManager : ILicenseManager
    {
        private static readonly int KeySize = 2048;
        private readonly RSACryptoServiceProvider cryptoProvider;

        public bool SecretKeyLoaded { get; }
        public static string DefaultDirectory { get; } = $@"{Directory.GetCurrentDirectory()}\Licensing\";
        public static string SignExtension { get; } = ".sig";
        internal static string secretKeyPath { get; } = $"{DefaultDirectory}licensing.skey";
        internal static string publicKeyPath { get; } = $"{DefaultDirectory}licensing.pkey";

        public License ActiveLicense { get; set; }

        public LicenseManager()
        {
            byte[] key;

            if (File.Exists(secretKeyPath))
            {
                key = File.ReadAllBytes(secretKeyPath);
                SecretKeyLoaded = true;
            }
            else// if (File.Exists(publicKeyPath))
            {
                //key = File.ReadAllBytes(publicKeyPath);
                key = ReadResource();
                SecretKeyLoaded = false;
            }
            //else
            //    throw new InvalidOperationException("Ошибка инициализации менеджера лицензий: не найден криптографический ключ.");

            cryptoProvider = new RSACryptoServiceProvider();
            cryptoProvider.ImportCspBlob(key);

            var licensePaths = new DirectoryInfo(DefaultDirectory).GetFiles("*.lic").OrderBy(x => x.CreationTime).ToList();

            if (licensePaths.Count > 0)
                ActiveLicense = LoadLicense(licensePaths.First().FullName);
        }

        internal static void CreateNewSigningKeyPair()
        {
            new FileInfo(DefaultDirectory).Directory.Create();

            using (var rsaProvider = new RSACryptoServiceProvider(KeySize))
            {
                var secretKey = rsaProvider.ExportCspBlob(true);
                File.WriteAllBytes(secretKeyPath, secretKey);

                var publicKey = rsaProvider.ExportCspBlob(false);
                File.WriteAllBytes(publicKeyPath, publicKey);
            }
        }

        public void SaveLicense(License license, string licensePath)
        {
            if (!SecretKeyLoaded)
                throw new InvalidOperationException("Ошибка генерации лицензии: отсутствует закрытый ключ.");

            var signPath = Path.ChangeExtension(licensePath, SignExtension);

            using (var licenseStream = new FileStream(licensePath, FileMode.CreateNew))
            using (var signStream = new FileStream(signPath, FileMode.CreateNew))
            {
                var formatter = new XmlSerializer(license.GetType());

                formatter.Serialize(licenseStream, license);

                licenseStream.Position = 0;

                var licenseSign = cryptoProvider.SignData(licenseStream, new SHA512CryptoServiceProvider());

                signStream.Write(licenseSign, 0, licenseSign.Length);
            }
        }

        public License LoadLicense(string licensePath)
        {
            License license = null;

            var signPath = Path.ChangeExtension(licensePath, SignExtension);

            using (var licenseStream = new FileStream(licensePath, FileMode.Open, FileAccess.Read))
            using (var signStream = new FileStream(signPath, FileMode.Open, FileAccess.Read))
            {
                var licenseBytes = GetBytes(licenseStream);
                var signBytes = GetBytes(signStream);

                if (cryptoProvider.VerifyData(licenseBytes, new SHA512CryptoServiceProvider(), signBytes))
                {
                    var formatter = new XmlSerializer(typeof(License));

                    licenseStream.Position = 0;
                    license = (License)formatter.Deserialize(licenseStream);
                }
                else
                    throw new InvalidOperationException("Ошибка проверки лицензии: подпись не соответствует лицензии.");
            }

            return license;
        }

        public string GetActiveLicenseInfo()
        {
            if (ActiveLicense == null)
                return "Отсутствует";

            var sb = new StringBuilder();

            sb.AppendLine($"Выдана: {ActiveLicense.Owner}");
            sb.AppendLine($"Активные разрешения:");
            sb.Append($@"Загрузка профилактических осмотров - ");

            if (ActiveLicense.ExaminationsUnlimited)
                sb.Append($"Без ограничений");
            else if (!string.IsNullOrEmpty(ActiveLicense.ExaminationsFomsCodeMO))
                sb.Append($"ЛПУ с кодом ФОМС {ActiveLicense.ExaminationsFomsCodeMO}");
            else if (ActiveLicense.ExaminationsMaxDate != null)
                sb.Append($"Дата осмотров до {ActiveLicense.ExaminationsMaxDate.Value.ToShortDateString()}");
            else
                sb.Append($"Недоступно");

            return sb.ToString();
        }

        private static byte[] GetBytes(Stream stream)
        {
            stream.Position = 0;

            byte[] result;

            using (var mStream = new MemoryStream())
            {
                stream.CopyTo(mStream);
                result = mStream.ToArray();
            }

            return result;
        }

        public byte[] ReadResource()
        {
            var assembly = Assembly.GetExecutingAssembly();

            var resourcePath = assembly.GetManifestResourceNames().Single(x => x.EndsWith("licensing.pkey"));

            byte[] result;

            using (var stream = assembly.GetManifestResourceStream(resourcePath))
            {
                result = GetBytes(stream);
            }

            return result;
        }
    }
}
