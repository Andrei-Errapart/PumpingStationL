using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PlcCommunication;

namespace ControlPanel
{
    public class SchemeStatement
    {
        public enum TYPE
        {
            SHOW,
            HIDE,
            BLINK,
            DISPLAY,
        };

        public readonly VisualizationControl Context;
        public readonly TYPE Type;
        public readonly SchemeLayer Layer;
        public readonly MuteButton MuteButton;
        public SchemeExpression ConditionOrExpression;

        public SchemeStatement(
            VisualizationControl Context,
            TYPE Type,
            string LayerName)
        {
            this.Context = Context;
            this.Type = Type;
            SchemeLayer layer = null;
            MuteButton mutebutton = null;
            Context.Layers.TryGetValue(LayerName, out layer);
            this.Layer = layer;
            if (layer == null)
            {
                Context.LogLine("Scheme program: layer '" + LayerName + "' not found, ignoring statement.");
            }
            else
            {
                if (this.Type == TYPE.BLINK)
                {
                    // alarm detected!
                    var bounds = layer.Layer.Bounds;
                    // New button.
                    var size = 48.0;
                    mutebutton = new MuteButton();
                    mutebutton.Opacity = 0.7;
                    mutebutton.SetValue(System.Windows.Controls.Canvas.LeftProperty, bounds.Right + size/4);
                    mutebutton.SetValue(System.Windows.Controls.Canvas.TopProperty, bounds.Top - size/2);
                    mutebutton.Width = size;
                    mutebutton.Height = size;
                    Context.mainCanvas.Children.Add(mutebutton);
                    mutebutton.SetAlarmState(false);
                }
                // Do we need the _GlyphRunDrawing ?
                if (this.Type == TYPE.DISPLAY)
                {
                    _GlyphRunDrawing = _FindGlyphRunDrawing(layer.Layer);
                    if (_GlyphRunDrawing == null)
                    {
                        // FIXME: do what?
                    }
                    else
                    {
                        _GlyphRun = _GlyphRunDrawing.GlyphRun;
                    }
                }
            }
            this.MuteButton = mutebutton;
        }

        /// <summary>
        /// Execute this statement. It is that simple.
        /// </summary>
        public void Execute()
        {
            _SourceSignals.Clear();
            int value = 0;
            if (Layer != null)
            {
                switch (Type)
                {
                    case TYPE.SHOW:
                        value = ConditionOrExpression == null ? 1 : ConditionOrExpression.Evaluate(_SourceSignals, true);
                        Layer.IsVisible = value != 0;
                        break;
                    case TYPE.HIDE:
                        value = ConditionOrExpression == null ? 1 : ConditionOrExpression.Evaluate(_SourceSignals, true);
                        Layer.IsVisible = value == 0;
                        break;
                    case TYPE.BLINK:
                        value = ConditionOrExpression == null ? 1 : ConditionOrExpression.Evaluate(_SourceSignals, true);
                        Layer.IsBlinking = value != 0;
                        if (MuteButton != null)
                        {
                            MuteButton.SetAlarmState(Layer.IsBlinking);
                        }
                        // Blinking objects must be invisible when not blinking....
                        if (Layer.IsBlinking)
                        {
                            // We shall mark the source signals.
                            foreach (var ss in _SourceSignals)
                            {
                                ss.DisplayIsAlarm = true;
                            }
                            _PrevSourceSignals.AddRange(_SourceSignals);
                        }
                        else
                        {
                            Layer.IsVisible = false;
                            // Previous source signals need unmarking.
                            foreach (var ss in _PrevSourceSignals)
                            {
                                ss.DisplayIsAlarm = false;
                            }
                            _PrevSourceSignals.Clear();
                        }
                        break;
                    case TYPE.DISPLAY:
                        if (_GlyphRunDrawing != null && ConditionOrExpression != null && ConditionOrExpression.Type==SchemeExpression.TYPE.UNARY_OP_SIGNAL)
                        {
                            var new_text = ConditionOrExpression.FetchSignal().DisplayReading;
                            if (new_text != _LastValue)
                            {
                                var ng = _BuildGlyphRun(new_text, _GlyphRunDrawing.GlyphRun);
                                _GlyphRunDrawing.GlyphRun = ng;
                                _LastValue = new_text;
                            }
                        }
                        break;
                }
            }
        }

        // Helper for TYPE.DISPLAY
        static GlyphRun _BuildGlyphRun(string text, GlyphRun template)
        {
            double fontSize = template.FontRenderingEmSize;
            GlyphRun glyphs = new GlyphRun();
            GlyphTypeface glyphFace = template.GlyphTypeface;
            System.ComponentModel.ISupportInitialize isi = glyphs;
            isi.BeginInit();
            glyphs.GlyphTypeface = glyphFace;
            glyphs.FontRenderingEmSize = fontSize;

            var textChars = text.ToCharArray();
            glyphs.Characters = textChars;
            var glyphIndices = new ushort[textChars.Length];
            var advancedWidths = new double[textChars.Length];
            for (int i = 0; i < textChars.Length; ++i)
            {
                var glyphIndex = glyphFace.CharacterToGlyphMap[textChars[i]];
                var glyphWidth = glyphFace.AdvanceWidths[glyphIndex];

                glyphIndices[i] = glyphIndex;
                advancedWidths[i] = glyphWidth * fontSize;
            }
            glyphs.GlyphIndices = glyphIndices;
            glyphs.AdvanceWidths = advancedWidths;
            glyphs.BaselineOrigin = template.BaselineOrigin;
            isi.EndInit();
            return glyphs;
        }

        // Helper for TYPE.DISPLAY
        static GlyphRunDrawing _FindGlyphRunDrawing(DrawingGroup dg)
        {
            foreach (var ch in dg.Children)
            {
                // Found the target?
                var grd = ch as GlyphRunDrawing;
                if (grd!=null)
                {
                    return grd;
                }
                // Search deeper?
                var dg2 = ch as DrawingGroup;
                if (dg2 != null)
                {
                    var grd2 = _FindGlyphRunDrawing(dg2);
                    if (grd2 != null)
                    {
                        return grd2;
                    }
                }
            }
            return null;
        }

        List<IOSignal> _SourceSignals = new List<IOSignal>();
        List<IOSignal> _PrevSourceSignals = new List<IOSignal>();
        System.Windows.Media.GlyphRunDrawing _GlyphRunDrawing = null;
        System.Windows.Media.GlyphRun _GlyphRun = null;
        // Don't build new GlyphRun when text is the same.
        string _LastValue = "";
    }
}
