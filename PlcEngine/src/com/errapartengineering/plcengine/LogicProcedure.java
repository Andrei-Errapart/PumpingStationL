package com.errapartengineering.plcengine;

import java.util.List;

public abstract class LogicProcedure {
	/// Arguments to the procedure.
	protected List<LogicExpression> argv;
	
	/// Constructor.
	public LogicProcedure(List<LogicExpression> argv)
	{
		this.argv = argv;
	}
	
	/// Call the procedure.
	public abstract void call();
}
