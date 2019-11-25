﻿using CHI.Services.AttachedPatients;
using PatientsFomsRepository.Infrastructure;
using Prism.Regions;
using Prism.Services.Dialogs;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace PatientsFomsRepository.ViewModels
{
    class ImportPatientsViewModel : DomainObject, IRegionMemberLifetime
    {
        #region Поля
        private IDialogService dialogService;
        private readonly IFileDialogService fileDialogService;
        #endregion

        #region Свойства
        public IMainRegionService MainRegionService { get; set; }
        public bool KeepAlive { get => false; }
        public DelegateCommandAsync ImportPatientsCommand { get; }
        public DelegateCommandAsync SaveExampleCommand { get; }
        public DelegateCommandAsync ClearDatabaseCommand { get; }
        #endregion

        #region Конструкторы
        public ImportPatientsViewModel(IMainRegionService mainRegionService, IFileDialogService fileDialogService, IDialogService dialogService)
        {
            MainRegionService = mainRegionService;
            this.fileDialogService = fileDialogService;
            this.dialogService = dialogService;

            MainRegionService.Header = "Загрузить известные ФИО из файла в базу данных";

            ImportPatientsCommand = new DelegateCommandAsync(ImportPatientsExecute);
            SaveExampleCommand = new DelegateCommandAsync(SaveExampleExecute);
            ClearDatabaseCommand = new DelegateCommandAsync(ClearDatabaseExecute);
        }
        #endregion

        #region Методы
        private void ImportPatientsExecute()
        {
            fileDialogService.DialogType = FileDialogType.Open;
            fileDialogService.Filter = "Excel files (*.xslx)|*.xlsx";

            if (fileDialogService.ShowDialog() != true)
                return;

            var importFilePath = fileDialogService.FullPath;

            MainRegionService.SetBusyStatus("Открытие файла.");

            var newPatients = PatientsFileService.ReadImportPatientsFile(importFilePath);

            MainRegionService.SetBusyStatus("Проверка значений.");
            var db = new Models.Database();
            db.Patients.Load();

            var existenInsuaranceNumbers = new HashSet<string>(db.Patients.Select(x => x.InsuranceNumber));
            var newUniqPatients = newPatients
            .Where(x => !existenInsuaranceNumbers.Contains(x.InsuranceNumber))
            .GroupBy(x => x.InsuranceNumber)
            .Select(x => x.First())
            .ToList();

            MainRegionService.SetBusyStatus("Сохранение в кэш.");
            db.Patients.AddRange(newUniqPatients);
            db.SaveChanges();

            int total = existenInsuaranceNumbers.Count + newUniqPatients.Count;
            MainRegionService.SetCompleteStatus($"В файле найдено {newPatients.Count} человек(а). В БД добавлено {newUniqPatients.Count} новых. Итого в БД {total}.");
        }
        private void SaveExampleExecute()
        {
            fileDialogService.DialogType = FileDialogType.Save;
            fileDialogService.FullPath = "Пример для загрузки ФИО";
            fileDialogService.Filter = "Excel files (*.xslx)|*.xlsx";

            if (fileDialogService.ShowDialog() != true)
                return;

            var saveExampleFilePath = fileDialogService.FullPath;

            MainRegionService.SetBusyStatus("Открытие файла.");
            PatientsFileService.SaveImportFileExample(saveExampleFilePath);
            MainRegionService.SetCompleteStatus($"Файл сохранен: {saveExampleFilePath}");
        }
        private void ClearDatabaseExecute()
        {
            var title = "Предупреждение";
            var message = "Информация о пациентах будет удалена из базы данных. Продолжить ?";
            var result = dialogService.ShowDialog(title, message);

            if (result == ButtonResult.Cancel)
                return;

            MainRegionService.SetBusyStatus("Очистка базы данных...");
            var db = new Models.Database();
            if (db.Database.Exists())
                db.Database.Delete();
            db.Database.Create();
            MainRegionService.SetCompleteStatus("База данных очищена.");
        }
        #endregion
    }
}
