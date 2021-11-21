package com.errapartengineering.plcengine;

import java.util.*;
import android.util.*;

/**
 * Logical program statement.
 * @author Andrei
 */
public final class LogicStatement {
    public enum TYPE {
        ASSIGNMENT,
        IF,
        PROCEDURE_CALL,
    }
    
    final Context Context;
    public final TYPE Type;
    public final IOSignal Destination;
    public final LogicExpression ExpressionOrCondition;
    public final List<LogicStatement> IfStatements;
    public final List<LogicStatement> ElseStatements;
    public final LogicProcedure Procedure;
    
    
    public LogicStatement(
            Context Context,
            TYPE Type,
            IOSignal Destination, LogicExpression ExpressionOrCondition,
            List<LogicStatement> IfStatements,
            List<LogicStatement> ElseStatements,
            LogicProcedure Procedure
            )
    {
        this.Context = Context;
        this.Type = Type;
        this.Destination = Destination;
        this.ExpressionOrCondition = ExpressionOrCondition;
        this.IfStatements = IfStatements;
        this.ElseStatements = ElseStatements;
        this.Procedure = Procedure;
    }
    
    public final void Execute()
    {
        switch (Type)
        {
            case ASSIGNMENT:
                {
                    int value = ExpressionOrCondition.Evaluate();
                    if (Destination != null)
                    {
                        Destination.setValue(value);
                    }
                }
                break;
            case IF:
                {
                    int value = ExpressionOrCondition.Evaluate();
                    if (value!=0)
                    {
                        // true!
                        for (LogicStatement ls : IfStatements)
                        {
                            ls.Execute();
                        }
                    }
                    else if (ElseStatements!=null)
                    {
                        // false, maybe.
                        for (LogicStatement ls : ElseStatements)
                        {
                            ls.Execute();
                        }
                    }
                }
                break;
            case PROCEDURE_CALL:
            	Procedure.call();
            	break;
            default:
                Log.d("LogicStatement", "Execute: Unexpected type: " + (Type) + "!");
                break;
        }
    }
}
