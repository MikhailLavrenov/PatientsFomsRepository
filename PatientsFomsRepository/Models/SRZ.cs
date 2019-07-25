﻿using FomsPatientsDB.Models;
using OfficeOpenXml;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PatientsFomsRepository.Models
    {

    /// <summary>
    /// Работа с веб-порталом СРЗ
    /// </summary>
    public class SRZ : IDisposable
        {
        #region Fields
        private HttpClient client;
        private Credential credential;
        #endregion

        #region Properties
        public bool Authorized { get; private set; }
        #endregion

        #region Creators
        public SRZ(string URL, string proxyAddress = null, int proxyPort = 0)
            {
            Authorized = false;
            var clientHandler = new HttpClientHandler();
            clientHandler.CookieContainer = new CookieContainer();
            if (proxyAddress != null && proxyPort != 0)
                {
                clientHandler.UseProxy = true;
                clientHandler.Proxy = new WebProxy($"{proxyAddress}:{proxyPort}");
                }
            client = new HttpClient(clientHandler);
            client.BaseAddress = new Uri(URL);
            }
        #endregion

        #region Methods
        //получает ссылку на файл заданной даты
        private async Task<string> GetFileReference(DateTime fileDate)
            {
            string shortFileDate = fileDate.ToShortDateString();
            var content = new FormUrlEncodedContent(new[]
                {
                new KeyValuePair<string, string>("export_date_on", shortFileDate),
                new KeyValuePair<string, string>("exportlist_id", "25"),
                });
            var response = await client.PostAsync("data/dbase.export.php", content);
            response.EnsureSuccessStatusCode();
            string responseText = response.Content.ReadAsStringAsync().Result;

            int begin = responseText.IndexOf(@"<a href='") + 9;
            int end = responseText.IndexOf(@"' ", begin) - begin;

            return responseText.Substring(begin, end);
            }
        //получает dbf файл прикрепленных пацентов
        private async Task<Stream> GetDbfFile(string downloadReference)
            {
            //скачиваем zip архив
            var response = await client.GetAsync(downloadReference);
            response.EnsureSuccessStatusCode();
            var zipFile = await response.Content.ReadAsStreamAsync();

            //извлекаем dbf файл
            Stream dbfFile = new MemoryStream();
            var archive = new ZipArchive(zipFile, ZipArchiveMode.Read);
            archive.Entries[0].Open().CopyTo(dbfFile);
            dbfFile.Position = 0;

            return dbfFile;
            }
        //преобразует dbf в excel
        private void DbfToExcel(Stream dbfFile, string excelFilePath)
            {
            using (var table = NDbfReader.Table.Open(dbfFile))
            using (var excel = new ExcelPackage())
                {
                var reader = table.OpenReader(Encoding.GetEncoding(866));
                var sheet = excel.Workbook.Worksheets.Add("Лист1");

                //вставляет заголовки и устанавливает формат столбцов
                for (int column = 0; column < table.Columns.Count; column++)
                    {
                    sheet.Cells[1, column + 1].Value = table.Columns[column].Name.ToString();

                    var type = table.Columns[column].Type;
                    type = Nullable.GetUnderlyingType(type) ?? type;
                    if (type.Name == "DateTime")
                        sheet.Column(column + 1).Style.Numberformat.Format = "dd.MM.yyyy";
                    }

                //заполняет строки таблицы
                int row = 2;
                while (reader.Read())
                    {
                    for (int column = 0; column < table.Columns.Count; column++)
                        sheet.Cells[row, column + 1].Value = reader.GetValue(table.Columns[column]);
                    row++;
                    }

                excel.SaveAs(new FileInfo(excelFilePath));
                }
            }
        //авторизация на сайте
        public bool TryAuthorize(Credential credential)
            {
            this.credential = credential;
            var content = new FormUrlEncodedContent(new[]
                {
                new KeyValuePair<string, string>("lg", credential.Login),
                new KeyValuePair<string, string>("pw", credential.Password),
                });
            try
                {
                var response = client.PostAsync("data/user.ajax.logon.php", content).Result;
                response.EnsureSuccessStatusCode();
                var responseText = response.Content.ReadAsStringAsync().Result;

                if (responseText == "")
                    {
                    Authorized = true;
                    return Authorized;
                    }
                else
                    throw new Exception();
                }
            catch (Exception)
                {
                Authorized = false;
                return Authorized;
                }
            }
        //выход с сайта
        public void Logout()
            {
            Authorized = false;
            var response = client.GetAsync("?show=logoff").Result;
            response.EnsureSuccessStatusCode();
            }
        //запрашивает данные пациента
        public bool TryGetPatient(string insuranceNumber, out Patient patient)
            {
            var content = new FormUrlEncodedContent(new[]
                {
                new KeyValuePair<string, string>("mode", "1"),
                new KeyValuePair<string, string>("person_enp", insuranceNumber),
                });

            try
                {
                var response = client.PostAsync("data/reg.person.polis.search.php", content).Result;
                response.EnsureSuccessStatusCode();
                var responseText = response.Content.ReadAsStringAsync().Result;
                var responseLines = responseText.Split(new string[] { "||" }, 7, StringSplitOptions.None);

                if (responseLines[0] != "0")
                    {
                    patient = new Patient(responseLines[2], responseLines[3], responseLines[4], responseLines[5]);
                    return true;
                    }
                else
                    throw new Exception();
                }
            catch (Exception)
                {
                patient = null;
                return false;
                }
            }
        //получает excel файл прикрепленных пациентов на дату
        public async Task GetPatientsFile(string excelFile, DateTime onDate)
            {
            var fileReference = await GetFileReference(onDate);
            var dbfFile = await GetDbfFile(fileReference);
            DbfToExcel(dbfFile, excelFile);
            }
        public void Dispose()
            {
            client.Dispose();
            }
        //запускает многопоточно запросы к сайту для поиска пациентов
        public static Patient[] GetPatients(string URL, string proxyAddress, int proxyPort, string[] insuranceNumbers, List<Credential> credentials, int threadsLimit)
            {
            if (insuranceNumbers.Length < threadsLimit)
                threadsLimit = insuranceNumbers.Length;

            var robinRoundCredentials = new RoundRobinCredentials(credentials);
            var verifiedPatients = new ConcurrentBag<Patient>();
            var tasks = new Task<SRZ>[threadsLimit];
            for (int i = 0; i < threadsLimit; i++)
                tasks[i] = Task.Run(() => { return (SRZ)null; });

            for (int i = 0; i < insuranceNumbers.Length; i++)
                {
                var insuranceNumber = insuranceNumbers[i];
                var index = Task.WaitAny(tasks);
                tasks[index] = tasks[index].ContinueWith((task) =>
                {
                    var site = task.Result;
                    if (site == null || site.credential.TryReserveRequest() == false)
                        {
                        if (site != null)
                            site.Logout();

                        while (true)
                            {
                            if (robinRoundCredentials.TryGetNext(out Credential credential) == false)
                                return null;

                            if (credential.TryReserveRequest())
                                {
                                site = new SRZ(URL, proxyAddress, proxyPort);
                                if (site.TryAuthorize(credential))
                                    break;
                                }
                            }
                        }

                    if (site.TryGetPatient(insuranceNumber, out Patient patient))
                        verifiedPatients.Add(patient);

                    return site;
                });
                }
            Task.WaitAll(tasks);

            return verifiedPatients.ToArray();
            }
        #endregion
        }
    }




























