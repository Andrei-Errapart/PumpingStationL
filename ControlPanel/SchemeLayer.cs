using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace ControlPanel
{
    public class SchemeLayer
    {
        public readonly string Name;
        public readonly DrawingGroup Layer;
        public bool IsBlinking;
        public bool IsVisible
        {
            get { return Layer.Opacity > 0; }
            set { Layer.Opacity = value ? 1.0 : 0.0; }
        }

        public SchemeLayer(string Name, DrawingGroup Layer)
        {
            this.Name = Name;
            this.Layer = Layer;
        }
    }
}
