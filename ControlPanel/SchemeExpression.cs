using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlcCommunication;

namespace ControlPanel
{
    public class SchemeExpression
    {
        public enum TYPE
        {
            LITERAL_INT,
            LITERAL_BOOLEAN,
            UNARY_OP_SIGNAL,
            UNARY_OP_IS_CONNECTED,
            BINARY_OP_AND,
            BINARY_OP_OR,
            BINARY_OP_XOR,
            BINARY_OP_LT,
            BINARY_OP_LE,
            BINARY_OP_GT,
            BINARY_OP_GE,
            UNARY_OP_NOT
        }

        readonly VisualizationControl Context;
        public readonly TYPE Type;
        readonly SchemeExpression X1;
        readonly SchemeExpression X2;

        readonly int SignalId;
        readonly string SignalName;
        readonly int LiteralValue;

        public SchemeExpression(
            VisualizationControl Context,
            TYPE Type,
            SchemeExpression X1, SchemeExpression X2,
            int SignalId, string SignalName,
            int LiteralValue
            )
        {
            this.Context = Context;
            this.Type = Type;
            this.X1 = X1;
            this.X2 = X2;
            this.SignalId = SignalId;
            this.SignalName = SignalName;
            this.LiteralValue = LiteralValue;
        }

        private int _EvaluateComparision(List<IOSignal> SourceSignals, bool AlarmIsPositive, Func<int, int, bool> Compare)
        {
            var ss = new List<IOSignal>();
            int r1 = X1.Evaluate(ss, true);
            int r2 = X2.Evaluate(ss, true);
            bool b = Compare(r1, r2);
            if (AlarmIsPositive == b)
            {
                SourceSignals.AddRange(ss);
            }
            return b ? 1 : 0;
        }

        /// <summary>
        /// Evaluate the expression. As a side effect, track signals which source non-zero values (these cause alarms).
        /// 
        /// Note: tracking of source signals of an XOR operation is not supported.
        /// </summary>
        /// <param name="SourceSignals">Tracked source signals. Will be empty when there is no alarm.</param>
        /// <param name="AlarmIsPositive">Is alarm level positive (true) or zero(false)?</param>
        /// <returns></returns>
        public int Evaluate(List<IOSignal> SourceSignals, bool AlarmIsPositive)
        {
            switch (Type)
            {
                case TYPE.LITERAL_INT:
                    return this.LiteralValue;
                case TYPE.LITERAL_BOOLEAN:
                    return this.LiteralValue;
                case TYPE.UNARY_OP_SIGNAL:
                    {
                        IOSignal ios = FetchSignal();
                        int r = ios == null ? 0 : ios.Value;
                        if (ios!=null && AlarmIsPositive == (r != 0))
                        {
                            SourceSignals.Add(ios);
                        }
                        return r;
                    }
                case TYPE.UNARY_OP_IS_CONNECTED:
                    {
                        IOSignal ios = FetchSignal();
                        int r = ios == null ? 0 : (ios.IsConnected ? 1 : 0);
                        if (ios != null && AlarmIsPositive == (r != 0))
                        {
                            SourceSignals.Add(ios);
                        }
                        return r;
                    }
                case TYPE.BINARY_OP_AND:
                    {
                        if (AlarmIsPositive)
                        {
                            var ss = new List<IOSignal>();
                            int r1 = X1.Evaluate(ss, true);
                            int r2 = X2.Evaluate(ss, true);
                            bool b = r1 != 0 && r2 != 0;
                            if (b)
                            {
                                SourceSignals.AddRange(ss);
                            }
                            return b ? 1 : 0;
                        }
                        else
                        {
                            int r1 = X1.Evaluate(SourceSignals, false);
                            int r2 = X2.Evaluate(SourceSignals, false);
                            return (r1 != 0 && r2 != 0) ? 1 : 0;
                        }
                    }
                case TYPE.BINARY_OP_OR:
                    {
                        if (AlarmIsPositive)
                        {
                            var r1 = X1.Evaluate(SourceSignals, true);
                            var r2 = X2.Evaluate(SourceSignals, true);
                            return (r1!=0 || r2!=0) ? 1 : 0;
                        }
                        else
                        {
                            var ss = new List<IOSignal>();
                            int r1 = X1.Evaluate(ss, false);
                            int r2 = X2.Evaluate(ss, false);
                            bool b = r1 != 0 || r2 != 0;
                            if (!b)
                            {
                                SourceSignals.AddRange(ss);
                            }
                            return b ? 1 : 0;
                        }
                    }
                    // return (X1.Evaluate() != 0) || (X2.Evaluate() != 0) ? 1 : 0;
                case TYPE.BINARY_OP_XOR:
                    {
                        var ss = new List<IOSignal>();
                        return (X1.Evaluate(ss, true) != 0) ^ (X2.Evaluate(ss, true) != 0) ? 1 : 0;
                    }
                case TYPE.BINARY_OP_LT:
                    return _EvaluateComparision(SourceSignals, AlarmIsPositive, (int x1, int x2) => { return x1 < x2; });
                case TYPE.BINARY_OP_LE:
                    return _EvaluateComparision(SourceSignals, AlarmIsPositive, (int x1, int x2) => { return x1 <= x2; });
                case TYPE.BINARY_OP_GT:
                    return _EvaluateComparision(SourceSignals, AlarmIsPositive, (int x1, int x2) => { return x1 > x2; });
                case TYPE.BINARY_OP_GE:
                    return _EvaluateComparision(SourceSignals, AlarmIsPositive, (int x1, int x2) => { return x1 >= x2; });
                case TYPE.UNARY_OP_NOT:
                    return (X1.Evaluate(SourceSignals, !AlarmIsPositive)!=0) ? 0 : 1;
                default:
                    Context.LogLine("SchemeExpression.Evaluate: Unexpected type: " + ((int)Type) + "!");
                    return 0;
            }
        }

        /// <summary>
        /// Fetch the signal. Complain when not found.
        /// </summary>
        /// <param name="SignalId"></param>
        /// <param name="SignalName"></param>
        /// <returns></returns>
        public IOSignal FetchSignal()
        {
            IOSignal ios;
            if (SignalId >= 0)
            {
                if (Context.SignalTable.TryGetValue(SignalId, out ios))
                {
                    return ios;
                }
                else
                {
                    Context.LogLine("Scheme program: No signal with id " + SignalId + ".");
                    return null;
                }
            }
            else if (SignalName != null && SignalName.Length > 0)
            {
                if (Context.SignalDict.TryGetValue(SignalName, out ios))
                {
                    return ios;
                }
                else
                {
                    Context.LogLine("Scheme program: No signal with name " + SignalName + ".");
                    return null;
                }
            }
            Context.LogLine("Scheme program: Signal requested, but no id nor name specified!");
            return null;
        }
    }
}
