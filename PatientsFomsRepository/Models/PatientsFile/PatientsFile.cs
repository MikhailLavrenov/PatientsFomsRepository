﻿using OfficeOpenXml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PatientsFomsRepository.Models
{
    /// <summary>
    /// Работа с excel файлом пациентов
    /// </summary>
    public class PatientsFile : IDisposable
    {
        #region Поля
        private static readonly object locker = new object();
        private ExcelPackage excel;
        private ExcelWorksheet sheet;
        private ColumnProperty[] columnProperties;
        private int maxRow;
        private int maxCol;
        private int headerIndex = 1;
        private int insuranceColumn;
        private int initialsColumn;
        private int surnameColumn;
        private int nameColumn;
        private int patronymicColumn;
        #endregion

        #region Методы
        //открывает файл
        public void Open(string filePath, ColumnProperty[] columnProperties = null)
        {
            excel = new ExcelPackage(new FileInfo(filePath));
            sheet = excel.Workbook.Worksheets[1];
            this.columnProperties = columnProperties ?? new ColumnProperty[0];
            maxRow = sheet.Dimension.Rows;
            maxCol = sheet.Dimension.Columns;
            insuranceColumn = GetColumnIndex("ENP");
            initialsColumn = GetColumnIndex("FIO");
            surnameColumn = GetColumnIndex("Фамилия");
            nameColumn = GetColumnIndex("Имя");
            patronymicColumn = GetColumnIndex("Отчество");

            FixStructure();
        }
        //сохраняет изменения в файл
        public void Save()
        {
            excel.Save();
        }
        //Возвращае полиса пациентов без полных ФИО
        public List<string> GetUnknownInsuaranceNumbers(int limitCount)
        {
            var patients = new List<string>();

            Parallel.For(headerIndex + 1, maxRow + 1, (row, state) =>
            {
                object insuranceValue;
                object initialsValue;
                object surnameValue;

                //lock (locker)
                {
                    insuranceValue = sheet.Cells[row, insuranceColumn].Value;
                    initialsValue = sheet.Cells[row, initialsColumn].Value;
                    surnameValue = sheet.Cells[row, surnameColumn].Value;
                }

                if (insuranceValue != null && initialsValue != null && surnameValue == null)
                {
                    lock (locker)
                    {
                        if (patients.Count < limitCount)
                            patients.Add(insuranceValue.ToString());
                        else
                            state.Break();
                    }
                }
            });

            return patients;
        }
        //вставляет полные ФИО в файл
        public void SetFullNames(IEnumerable<Patient> cachedPatients)
        {
            Parallel.For(headerIndex + 1, maxRow + 1, (row, state) =>
            {
                object insuranceValue;
                object initialsValue;
                object surnameValue;

                //lock (locker)
                {
                    insuranceValue = sheet.Cells[row, insuranceColumn].Value;
                    initialsValue = sheet.Cells[row, initialsColumn].Value;
                    surnameValue = sheet.Cells[row, surnameColumn].Value;
                }

                if (insuranceValue != null && initialsValue != null && surnameValue == null)
                {
                    var patient = cachedPatients.Where(x => x.InsuranceNumber == insuranceValue.ToString() && x.Initials == initialsValue.ToString()).FirstOrDefault();

                    if (patient != null)
                        lock (locker)
                        {
                            sheet.Cells[row, surnameColumn].Value = patient.Surname;
                            sheet.Cells[row, nameColumn].Value = patient.Name;
                            sheet.Cells[row, patronymicColumn].Value = patient.Patronymic;
                        }
                }
            });
        }
        //Применяет все форматирования
        public void Format()
        {
            ApplyColumnProperty();
            SetColumnsOrder();
            RenameSex();
            sheet.Cells.AutoFitColumns();
            sheet.Cells[sheet.Dimension.Address].AutoFilter = true;
        }
        //освобождение неуправляемых ресурсов
        public void Dispose()
        {
            sheet?.Dispose();
            excel?.Dispose();
        }
        //ищет номер столбца по заголовку или его алтернативному названию, если столбец не найден возвращает -1
        private int GetColumnIndex(string columnName)
        {
            var columnProperty = columnProperties
                .Where(x => x.Name == columnName || x.AltName == columnName)
                .FirstOrDefault();

            if (columnProperty == null)
                columnProperty = new ColumnProperty() { Name = columnName, AltName = columnName };

            return GetColumnIndex(columnProperty);
        }
        private int GetColumnIndex(ColumnProperty columnProperty)
        {
            for (int col = 1; col <= maxCol; col++)
            {
                var cellValue = sheet.Cells[headerIndex, col].Value;

                if (cellValue == null)
                    continue;

                var cellText = cellValue.ToString();
                if ((cellText == columnProperty.Name) || (cellText == columnProperty.AltName))
                    return col;
            }
            return -1;
        }
        //возвращает атрибуты столбца по имени, если такого нет - возвращает новый экземпляр с таким именем
        private ColumnProperty GetColumnProperty(string name)
        {
            foreach (var attribute in columnProperties)
                if ((attribute.Name == name) || (attribute.AltName == name))
                    return attribute;

            return new ColumnProperty { Name = name, AltName = name, Hide = false, Delete = false };
        }
        //проверяет структуру файла, при необходимости добавляет столбцы Фамилия, Имя, Отчество
        private void FixStructure()
        {
            if (insuranceColumn == -1)
                throw new Exception("Не найден столбец с номером полиса");

            if (initialsColumn == -1)
                throw new Exception("Не найден столбец с инициалами ФИО");

            if (surnameColumn == -1)
            {
                surnameColumn = initialsColumn + 1;
                sheet.InsertColumn(surnameColumn, 1);
                sheet.Cells[headerIndex, surnameColumn].Value = "Фамилия";
            }

            if (nameColumn == -1)
            {
                nameColumn = surnameColumn + 1;
                sheet.InsertColumn(nameColumn, 1);
                sheet.Cells[headerIndex, nameColumn].Value = "Имя";
            }

            if (patronymicColumn == -1)
            {
                patronymicColumn = nameColumn + 1;
                sheet.InsertColumn(patronymicColumn, 1);
                sheet.Cells[headerIndex, patronymicColumn].Value = "Отчество";
            }
        }
        //изменяет порядок столбоцов в соотвествии с порядком следования ColumnProperties
        private void SetColumnsOrder()
        {
            int correctIndex = 1;

            foreach (var columnProperty in columnProperties)
            {
                var currentIndex = GetColumnIndex(columnProperty);

                //если столбец на своем месте 
                if (currentIndex == correctIndex)
                    correctIndex++;
                //если столбец не на своем месте и столбец найден в таблице
                else if (currentIndex != -1)
                {
                    sheet.InsertColumn(correctIndex, 1);
                    currentIndex++;
                    var currentRange = sheet.Cells[headerIndex, currentIndex, maxRow, currentIndex];
                    var correctRange = sheet.Cells[headerIndex, correctIndex, maxRow, correctIndex];
                    currentRange.Copy(correctRange);
                    sheet.DeleteColumn(currentIndex);
                    correctIndex++;
                }
            }
        }
        //переименовывает цифры с полом в нормальные названия
        private void RenameSex()
        {
            int sexColumn = GetColumnIndex("SEX");

            if (sexColumn == -1)
                return;

            var cells = sheet.Cells[headerIndex + 1, sexColumn, maxRow, sexColumn]
                .Where(x => x.Value != null);

            foreach (var item in cells)
                item.ToString()
                    .Replace("1", "Мужской")
                    .Replace("2", "Женский");


            //if (sexColumn != -1)
            //    for (int i = 1; i <= maxRow; i++)
            //    {
            //        var cellValue = sheet.Cells[i, sexColumn].Value;

            //        if (cellValue == null)
            //            continue;

            //        var str = cellValue.ToString();
            //        if (str == "1")
            //            sheet.Cells[i, sexColumn].Value = "Мужской";
            //        else if (str == "2")
            //            sheet.Cells[i, sexColumn].Value = "Женский";
            //    }
        }
        //применяет свойства столбца к таблице: заменяет названия столбцов на русские, скрывает и удаляет столбцы
        private void ApplyColumnProperty()
        {
            for (int i = 1; i <= maxCol; i++)
            {
                var cellValue = sheet.Cells[headerIndex, i].Value;

                if (cellValue == null)
                    continue;

                var name = cellValue.ToString();
                var synonim = GetColumnProperty(name);

                if (name != synonim.AltName)
                    sheet.Cells[headerIndex, i].Value = synonim.AltName;

                if (synonim.Hide)
                    sheet.Column(i).Hidden = true;

                if (synonim.Delete)
                {
                    sheet.DeleteColumn(i);
                    maxCol--;
                    i--;
                }
            }
        }
        #endregion
    }


}
