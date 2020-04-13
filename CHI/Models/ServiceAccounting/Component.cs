﻿using CHI.Models.Infrastructure;
using System.Collections.Generic;
using System.Windows.Media;

namespace CHI.Models.ServiceAccounting
{
    public class Component : IOrderedHierarchical<Component>
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
        public string HexColor { get; set; } = "#FFFFFF";
        public bool IsRoot { get; set; } = false;
        public bool IsCanPlanning { get; set; }
        public List<CaseFilter> CaseFilters { get; set; }
        public List<Indicator> Indicators { get; set; }

        public Component Parent { get; set; }
        public List<Component> Childs { get; set; }

    }
}
