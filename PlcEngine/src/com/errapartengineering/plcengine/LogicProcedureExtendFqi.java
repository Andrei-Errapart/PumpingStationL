package com.errapartengineering.plcengine;

import java.util.List;

import android.util.Log;

public class LogicProcedureExtendFqi extends LogicProcedure {
	public LogicProcedureExtendFqi(List<LogicExpression> argv)
	{
		super(argv);
	}
	
	/// Extend the 16-bit FQI count to 32 bits.
	/// argv[0]: Register to be extended, must be a variable of type REGISTER32.
	/// argv[1]: FQI register.
	/// argv[2]: Offset, must be a variable of type REGISTER32.
	@Override
	public void call()
	{
		if (argv.size()>=3)
		{
			LogicExpression ex_result = this.argv.get(0);
			LogicExpression ex_ofs = this.argv.get(2);
			if (ex_result.Type == LogicExpression.TYPE.UNARY_OP_SIGNAL && ex_ofs.Type==LogicExpression.TYPE.UNARY_OP_SIGNAL)
			{
				int fqi32 = ex_result.Signal.getValue();
				int ofs = ex_ofs.Signal.getValue();
				int fqi16 = this.argv.get(1).Evaluate();
				int ofs_old = ofs;
				
				int fqi16_prev = (fqi32 - ofs) & 0xFFFF;
				// Is new offset needed?
				if (fqi16_prev > fqi16)
				{
					// New offset needed!
					ofs = ofs + (fqi16_prev - fqi16);
					ex_ofs.Signal.setValue(ofs);
				}
				int fqi32_new = fqi16 + ofs;
				ex_result.Signal.setValue(fqi32_new);
			}
		}
	}
}
