using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DumpSignalValues
{
    public class ComputedSignal : IOSignal
    {
        public enum COMPUTATION_TYPE {
            ANALOG_SENSOR,
            FLOATING_POINT_VALUE,
            PUMP_HOURS_OF_OPERATION
        };

        public readonly COMPUTATION_TYPE ComputationType;
        public readonly string Unit;
        public readonly string FormatString;
        public readonly string[] SourceSignalNames;
        public IOSignal[] SourceSignals;

        public ComputedSignal(
            string Name,
            COMPUTATION_TYPE ComputationType,
            string[] SourceSignalNames,
            string Parameters,
            string FormatString,
            string Unit,
            string Description)
            : base(0, Name, true, TYPE.INPUT_REGISTER, "", "", Description, "", "")
        {
            this.ComputationType = ComputationType;
            this.Unit = Unit;
            this.FormatString = FormatString;
            this.SourceSignalNames = SourceSignalNames;
            this._Values = new int[SourceSignalNames.Length];

            switch (this.ComputationType)
            {
                case COMPUTATION_TYPE.ANALOG_SENSOR:
                    var v = (from s in (Parameters.Split(new [] { ';'})) select double.Parse(s.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture) ).ToArray();
                    _CurrentZero = v[0];
                    _CurrentMax = v[1];
                    _ReadingZero = v[2];
                    _ReadingMax = v[3];
                    break;
                case COMPUTATION_TYPE.FLOATING_POINT_VALUE:
                    _ByteValues = new byte[4];
                    break;
                case COMPUTATION_TYPE.PUMP_HOURS_OF_OPERATION:
                    break;
            }
        }

        protected ComputedSignal(ComputedSignal src)
            : base(src)
        {
            this.ComputationType = src.ComputationType;
            this.Unit = src.Unit;
            this.FormatString = src.FormatString;
            this.SourceSignalNames = src.SourceSignalNames;
            this._Values = new int[SourceSignalNames.Length];
            this._CurrentZero = src._CurrentZero;
            this._CurrentMax = src._CurrentMax;
            this._ReadingZero = src._ReadingZero;
            this._ReadingMax = src._ReadingMax;
            if (src._ByteValues != null)
            {
                this._ByteValues = new byte[src._ByteValues.Length];
            }
        }

        public void ConnectWithSourceSignals(Dictionary<string, IOSignal> SignalDict)
        {
            this.SourceSignals = (from signal_name in SourceSignalNames select SignalDict[signal_name]).ToArray();
        }

        public void UpdateValueOnly()
        {
            for (int i = 0; i < SourceSignals.Length; ++i)
            {
                _Values[i] = SourceSignals[i].Value;
            }
            switch (this.ComputationType)
            {
                case COMPUTATION_TYPE.ANALOG_SENSOR:
                    {
                        int MAX_ADC_READING = 32768;
                        int sensor_reading = _Values[0];
                        if (sensor_reading >= 0x8000)
                        {
                            unchecked
                            {
                                sensor_reading = sensor_reading | (int)0xFFFF0000;
                            }
                        }
                        double current = sensor_reading * _CurrentMax / MAX_ADC_READING;
                        double reading = (current - _CurrentZero) / (_CurrentMax - _CurrentZero) * (_ReadingMax - _ReadingZero) + _ReadingZero;
                        this._RealValue = reading;
                    }
                    break;
                case COMPUTATION_TYPE.FLOATING_POINT_VALUE:
                    {
                        int n = _Values.Length / 2;
                        double sum = 0;
                        for (int i = 0; i < n; ++i)
                        {
                            int ofs = 2 * i;
                            _ByteValues[0] = (byte)(_Values[ofs + 1] & 0xFF);
                            _ByteValues[1] = (byte)((_Values[ofs + 1] >> 8) & 0xFF);
                            _ByteValues[2] = (byte)(_Values[ofs + 0] & 0xFF);
                            _ByteValues[3] = (byte)((_Values[ofs + 0] >> 8) & 0xFF);
                            double reading = BitConverter.ToSingle(_ByteValues, 0);
                            sum += reading;
                        }
                        this._RealValue = sum;
                    }
                    break;
                case COMPUTATION_TYPE.PUMP_HOURS_OF_OPERATION:
                    {
                        _PumpHours = (_Values[0] << 16) + _Values[1];
                        _PumpMinutes = _Values[2];
                        this._RealValue = _PumpHours + (1.0 / 60.0 * _PumpMinutes);
                    }
                    break;
            }
            base._IsConnected = true;
            base.DisplayIsConnected = true;
        }

        public void Update()
        {
            UpdateValueOnly();
            switch (this.ComputationType)
            {
                case COMPUTATION_TYPE.ANALOG_SENSOR:
                    this.DisplayReading = _RealValue.ToString(FormatString) + (Unit.Length == 0 ? "" : " ") + Unit;
                    break;
                case COMPUTATION_TYPE.FLOATING_POINT_VALUE:
                    this.DisplayReading = _RealValue.ToString(FormatString) + (Unit.Length == 0 ? "" : " ") + Unit;
                    break;
                case COMPUTATION_TYPE.PUMP_HOURS_OF_OPERATION:
                    this.DisplayReading = _PumpHours.ToString() + "h " + _PumpMinutes.ToString("00") + "m";
                    break;
            }
            base._IsConnected = true;
            base.DisplayIsConnected = true;
        }

        public static COMPUTATION_TYPE ComputationTypeOf(string s)
        {
            if (string.Compare(s, "ANALOGSENSOR", true) == 0)
            {
                return COMPUTATION_TYPE.ANALOG_SENSOR;
            }
            else if (string.Compare(s, "FLOATINGPOINTVALUE", true) == 0)
            {
                return COMPUTATION_TYPE.FLOATING_POINT_VALUE;
            }
            else if (string.Compare(s, "PUMPHOURSOFOPERATION", true) == 0)
            {
                return COMPUTATION_TYPE.PUMP_HOURS_OF_OPERATION;
            }
            else
            {
                throw new ApplicationException("COMPUTATION_TYPE.ComputationTypeOf: Unknown type '" + s + "'");
            }
        }

        public override IOSignal CloneForChart()
        {
            var r = new ComputedSignal(this);
            return r;
        }

        int[] _Values;
        byte[] _ByteValues;
        double _CurrentZero = 0.0;
        double _CurrentMax = 20.0;
        double _ReadingZero = 0.0;
        double _ReadingMax = 100.0;
        int _PumpHours = 0;
        int _PumpMinutes = 0;
    }
}
