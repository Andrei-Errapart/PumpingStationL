package com.errapartengineering.plcengine;

import android.util.*; // Log.d

/**
 *
 * @author Andrei
 */
public final class LogicExpression {
    public enum TYPE {
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
    
    final Context Context;
    final TYPE Type;
    
    final LogicExpression X1;
    final LogicExpression X2;

    final IOSignal Signal;
    final ComputedSignal ComputedSignal;
    final int LiteralValue;

    public LogicExpression(
        Context Context,
        TYPE Type,
        LogicExpression X1, LogicExpression X2,
        IOSignal Signal,
        int LiteralValue
        )
    {
        this.Context = Context;
        this.Type = Type;
        this.X1 = X1;
        this.X2 = X2;
        this.Signal = Signal;
        this.ComputedSignal =  Signal!=null && Signal instanceof com.errapartengineering.plcengine.ComputedSignal ?  (ComputedSignal)Signal : null;
        this.LiteralValue = LiteralValue;
    }
    
    public final double EvaluateFloat()
    {
    	if (Type == TYPE.UNARY_OP_SIGNAL) {
        	if (ComputedSignal!=null) {
        		return ComputedSignal.FloatingPointValue;
        	}
        	if (Signal != null) {
        		return Signal.getValue();
        	}
        	return 0;
        } else {
        	return Evaluate();
        }
    }
    
    public final int Evaluate()
    {
        switch (Type)
        {
            case LITERAL_INT:
                return this.LiteralValue;
            case LITERAL_BOOLEAN:
                return this.LiteralValue;
            case UNARY_OP_SIGNAL:
                return Signal == null ? 0 : Signal.getValue();
            case UNARY_OP_IS_CONNECTED:
                // Signals with no device are always connected!
                return Signal == null
                        ? 0
                        : (Signal.Device==null ? 1 : (Signal.Device.IsLastSyncOk ? 1 : 0));
            case BINARY_OP_AND:
            {
            	int i1 = X1.Evaluate();
            	int i2 = X2.Evaluate();
                return (i1!=0) && (i2!=0) ? 1 : 0;
            }
            case BINARY_OP_OR:
            {
            	int i1 = X1.Evaluate();
            	int i2 = X2.Evaluate();
                return (i1!=0) || (i2!=0) ? 1 : 0;
            }
            case BINARY_OP_XOR:
                return (X1.Evaluate() != 0) ^ (X2.Evaluate() != 0) ? 1 : 0;
            case BINARY_OP_LT:
            {
            	double x1 = X1.EvaluateFloat();
            	double x2 = X2.EvaluateFloat();
                return x1 < x2 ? 1 : 0;
            }
            case BINARY_OP_LE:
                return X1.EvaluateFloat() <= X2.EvaluateFloat() ? 1 : 0;
            case BINARY_OP_GT:
            {
            	double x1 = X1.EvaluateFloat();
            	double x2 = X2.EvaluateFloat();
                return x1 > x2 ? 1 : 0;
            }
            case BINARY_OP_GE:
                return X1.EvaluateFloat() >= X2.EvaluateFloat() ? 1 : 0;
            case UNARY_OP_NOT:
                return (X1.Evaluate()!=0) ? 0 : 1;
            default:
                Log.d("LogicExpression", "Evaluate: Unexpected type: " + (Type) + "!");
                return 0;
        }
    }
}
