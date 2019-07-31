﻿using FomsPatientsDB.Models;
using PatientsFomsRepository.Infrastructure;
using PatientsFomsRepository.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace PatientsFomsRepository.ViewModels
    {
    public class PatientsFileSettingsViewModel : BindableBase, IViewModel
        {
        #region Fields
        private Settings settings;
        #endregion

        #region Properties
        public string ShortCaption { get; set; }
        public string FullCaption { get; set; }
        public Settings Settings { get => settings; set => SetProperty(ref settings, value); }
        public RelayCommand SaveCommand { get; }
        public RelayCommand LoadCommand { get; }
        public RelayCommand SetDefaultCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        #endregion

        #region Creators
        public PatientsFileSettingsViewModel()
            {
            ShortCaption = "Настройки файла пациентов";
            FullCaption = "Настройка выгрузки файла пациентов";
            SaveCommand = new RelayCommand(x => Settings.Save());
            LoadCommand = new RelayCommand(x => Settings = Settings.Load());            
            SetDefaultCommand = new RelayCommand(x => Settings.PatiensFileSetDefault());
            MoveUpCommand = new RelayCommand(x=> Settings.MoveColumnPropertyUp(x as ColumnProperty));
            MoveDownCommand = new RelayCommand(x => Settings.MoveColumnPropertyDown(x as ColumnProperty));

            Settings = Settings.Load();
            }
        #endregion

        #region Methods
        #endregion
        }
    }
;