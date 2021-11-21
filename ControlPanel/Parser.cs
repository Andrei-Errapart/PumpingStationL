using System.Collections.Generic;



using System;

namespace ControlPanel {



public class Parser {
	public const int _EOF = 0;
	public const int _string = 1;
	public const int _intliteral = 2;
	public const int _ident = 3;
	public const int maxT = 26;

	const bool T = true;
	const bool x = false;
	const int minErrDist = 2;
	
	public Scanner scanner;
	public Errors  errors;

	public Token t;    // last recognized token
	public Token la;   // lookahead token
	int errDist = minErrDist;

public List<SchemeStatement> Result;
public VisualizationControl Context;

public SchemeExpression ConcatExpression(SchemeExpression expr, SchemeExpression.TYPE op, SchemeExpression expr2)
{
	var r = new SchemeExpression(Context, op, expr, expr2, -1, null, -1);
	return r;
}

public SchemeExpression ConcatUnaryExpression(SchemeExpression.TYPE op, SchemeExpression expr)
{
	var r = new SchemeExpression(Context, op, expr, null, -1, null, -1);
	return r;
}



	public Parser(Scanner scanner) {
		this.scanner = scanner;
		errors = new Errors();
	}

	void SynErr (int n) {
		if (errDist >= minErrDist) errors.SynErr(la.line, la.col, n);
		errDist = 0;
	}

	public void SemErr (string msg) {
		if (errDist >= minErrDist) errors.SemErr(t.line, t.col, msg);
		errDist = 0;
	}
	
	void Get () {
		for (;;) {
			t = la;
			la = scanner.Scan();
			if (la.kind <= maxT) { ++errDist; break; }

			la = t;
		}
	}
	
	void Expect (int n) {
		if (la.kind==n) Get(); else { SynErr(n); }
	}
	
	bool StartOf (int s) {
		return set[s, la.kind];
	}
	
	void ExpectWeak (int n, int follow) {
		if (la.kind == n) Get();
		else {
			SynErr(n);
			while (!StartOf(follow)) Get();
		}
	}


	bool WeakSeparator(int n, int syFol, int repFol) {
		int kind = la.kind;
		if (kind == n) {Get(); return true;}
		else if (StartOf(repFol)) {return false;}
		else {
			SynErr(n);
			while (!(set[syFol, kind] || set[repFol, kind] || set[0, kind])) {
				Get();
				kind = la.kind;
			}
			return StartOf(syFol);
		}
	}

	
	void SchemeProgram() {
		SchemeStatement st = null; 
		SchemeStatement1(ref st);
		Result.Add(st); 
		while (StartOf(1)) {
			SchemeStatement1(ref st);
			Result.Add(st); 
		}
	}

	void SchemeStatement1(ref SchemeStatement st) {
		SchemeStatement.TYPE type = SchemeStatement.TYPE.SHOW;
		SchemeExpression expr = null; 
		if (la.kind == 8 || la.kind == 9 || la.kind == 10) {
			ShowHideOrBlinkKw(ref type);
			Expect(3);
			st = new SchemeStatement(Context, type, t.val); 
			if (la.kind == 4) {
				Get();
				Expr(ref expr);
			}
			st.ConditionOrExpression = expr; 
		} else if (la.kind == 5) {
			Get();
			Expect(3);
			st = new SchemeStatement(Context, SchemeStatement.TYPE.DISPLAY, t.val); 
			Expect(6);
			Expr(ref expr);
			st.ConditionOrExpression = expr; 
		} else SynErr(27);
		Expect(7);
	}

	void ShowHideOrBlinkKw(ref SchemeStatement.TYPE type) {
		if (la.kind == 8) {
			Get();
			type = SchemeStatement.TYPE.SHOW; 
		} else if (la.kind == 9) {
			Get();
			type = SchemeStatement.TYPE.HIDE; 
		} else if (la.kind == 10) {
			Get();
			type = SchemeStatement.TYPE.BLINK; 
		} else SynErr(28);
	}

	void Expr(ref SchemeExpression exp) {
		SchemeExpression exp2 = null;
		SchemeExpression.TYPE op = SchemeExpression.TYPE.LITERAL_INT; 
		AndExp(ref exp);
		while (la.kind == 20 || la.kind == 21) {
			OrOp(ref op);
			AndExp(ref exp2);
			exp = ConcatExpression(exp, op, exp2); 
		}
	}

	void Number(ref SchemeExpression num) {
		Expect(2);
		num = new SchemeExpression(Context, SchemeExpression.TYPE.LITERAL_INT, null, null, -1, null,  Convert.ToInt32(t.val)); 
	}

	void TrueOrFalse(ref SchemeExpression b) {
		if (la.kind == 11) {
			Get();
			b = new SchemeExpression(Context, SchemeExpression.TYPE.LITERAL_INT, null, null, -1, null,  1); 
		} else if (la.kind == 12) {
			Get();
			b = new SchemeExpression(Context, SchemeExpression.TYPE.LITERAL_INT, null, null, -1, null,  0); 
		} else SynErr(29);
	}

	void SignalOrIsConnected(ref SchemeExpression sig) {
		SchemeExpression.TYPE type = SchemeExpression.TYPE.UNARY_OP_SIGNAL; 
		if (la.kind == 14 || la.kind == 15) {
			SignalOp(ref type);
			if (la.kind == 3) {
				Get();
				sig = new SchemeExpression(Context, type, null, null, -1, t.val,  -1); 
			} else if (la.kind == 2) {
				Get();
				sig = new SchemeExpression(Context, type, null, null, Convert.ToInt32(t.val), null,  -1); 
			} else SynErr(30);
		} else if (la.kind == 3) {
			Get();
			sig = new SchemeExpression(Context, type, null, null, -1, t.val,  -1); 
		} else if (la.kind == 13) {
			Get();
			if (la.kind == 3) {
				Get();
				sig = ConcatUnaryExpression(SchemeExpression.TYPE.UNARY_OP_NOT, new SchemeExpression(Context, SchemeExpression.TYPE.UNARY_OP_IS_CONNECTED, null, null, -1, t.val,  -1)); 
			} else if (la.kind == 2) {
				Get();
				sig = ConcatUnaryExpression(SchemeExpression.TYPE.UNARY_OP_NOT, new SchemeExpression(Context, SchemeExpression.TYPE.UNARY_OP_IS_CONNECTED, null, null, Convert.ToInt32(t.val), null,  -1)); 
			} else SynErr(31);
		} else SynErr(32);
	}

	void SignalOp(ref SchemeExpression.TYPE type) {
		if (la.kind == 14) {
			Get();
			type = SchemeExpression.TYPE.UNARY_OP_SIGNAL; 
		} else if (la.kind == 15) {
			Get();
			type = SchemeExpression.TYPE.UNARY_OP_IS_CONNECTED; 
		} else SynErr(33);
	}

	void AndExp(ref SchemeExpression exp) {
		SchemeExpression exp2 = null;
		SchemeExpression.TYPE op = SchemeExpression.TYPE.LITERAL_INT; 
		NotExp(ref exp);
		while (la.kind == 19) {
			AndOp(ref op);
			NotExp(ref exp2);
			exp = ConcatExpression(exp, op, exp2); 
		}
	}

	void OrOp(ref SchemeExpression.TYPE op) {
		if (la.kind == 20) {
			Get();
			op = SchemeExpression.TYPE.BINARY_OP_OR; 
		} else if (la.kind == 21) {
			Get();
			op = SchemeExpression.TYPE.BINARY_OP_XOR; 
		} else SynErr(34);
	}

	void NotExp(ref SchemeExpression exp) {
		SchemeExpression exp2 = null; 
		if (StartOf(2)) {
			RelExp(ref exp);
		} else if (la.kind == 16) {
			Get();
			RelExp(ref exp2);
			exp = ConcatUnaryExpression(SchemeExpression.TYPE.UNARY_OP_NOT, exp2); 
		} else SynErr(35);
	}

	void AndOp(ref SchemeExpression.TYPE op) {
		Expect(19);
		op = SchemeExpression.TYPE.BINARY_OP_AND; 
	}

	void RelExp(ref SchemeExpression exp) {
		exp = null;
		SchemeExpression exp2 = null;
		SchemeExpression.TYPE op = SchemeExpression.TYPE.LITERAL_INT; 
		Value(ref exp);
		while (StartOf(3)) {
			RelOp(ref op);
			Value(ref exp2);
			exp = ConcatExpression(exp, op, exp2); 
		}
	}

	void Value(ref SchemeExpression exp) {
		if (la.kind == 11 || la.kind == 12) {
			TrueOrFalse(ref exp);
		} else if (la.kind == 2) {
			Number(ref exp);
		} else if (StartOf(4)) {
			SignalOrIsConnected(ref exp);
		} else if (la.kind == 17) {
			Get();
			Expr(ref exp);
			Expect(18);
		} else SynErr(36);
	}

	void RelOp(ref SchemeExpression.TYPE op) {
		op = SchemeExpression.TYPE.BINARY_OP_LT; 
		if (la.kind == 22) {
			Get();
			op = SchemeExpression.TYPE.BINARY_OP_LT; 
		} else if (la.kind == 23) {
			Get();
			op = SchemeExpression.TYPE.BINARY_OP_LE; 
		} else if (la.kind == 24) {
			Get();
			op = SchemeExpression.TYPE.BINARY_OP_GT; 
		} else if (la.kind == 25) {
			Get();
			op = SchemeExpression.TYPE.BINARY_OP_GE; 
		} else SynErr(37);
	}



	public void Parse() {
		la = new Token();
		la.val = "";		
		Get();
		SchemeProgram();
		Expect(0);

	}
	
	static readonly bool[,] set = {
		{T,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x},
		{x,x,x,x, x,T,x,x, T,T,T,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x},
		{x,x,T,T, x,x,x,x, x,x,x,T, T,T,T,T, x,T,x,x, x,x,x,x, x,x,x,x},
		{x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,x,x, x,x,T,T, T,T,x,x},
		{x,x,x,T, x,x,x,x, x,x,x,x, x,T,T,T, x,x,x,x, x,x,x,x, x,x,x,x}

	};
} // end Parser


public class Errors {
	public int count = 0;                                    // number of errors detected
	public System.IO.TextWriter errorStream = Console.Out;   // error messages go to this stream
	public string errMsgFormat = "-- line {0} col {1}: {2}"; // 0=line, 1=column, 2=text

	public virtual void SynErr (int line, int col, int n) {
		string s;
		switch (n) {
			case 0: s = "EOF expected"; break;
			case 1: s = "string expected"; break;
			case 2: s = "intliteral expected"; break;
			case 3: s = "ident expected"; break;
			case 4: s = "\"When\" expected"; break;
			case 5: s = "\"Display\" expected"; break;
			case 6: s = "\"As\" expected"; break;
			case 7: s = "\";\" expected"; break;
			case 8: s = "\"Show\" expected"; break;
			case 9: s = "\"Hide\" expected"; break;
			case 10: s = "\"Blink\" expected"; break;
			case 11: s = "\"True\" expected"; break;
			case 12: s = "\"False\" expected"; break;
			case 13: s = "\"NotConnected\" expected"; break;
			case 14: s = "\"Signal\" expected"; break;
			case 15: s = "\"IsConnected\" expected"; break;
			case 16: s = "\"Not\" expected"; break;
			case 17: s = "\"(\" expected"; break;
			case 18: s = "\")\" expected"; break;
			case 19: s = "\"And\" expected"; break;
			case 20: s = "\"Or\" expected"; break;
			case 21: s = "\"Xor\" expected"; break;
			case 22: s = "\"<\" expected"; break;
			case 23: s = "\"<=\" expected"; break;
			case 24: s = "\">\" expected"; break;
			case 25: s = "\">=\" expected"; break;
			case 26: s = "??? expected"; break;
			case 27: s = "invalid SchemeStatement1"; break;
			case 28: s = "invalid ShowHideOrBlinkKw"; break;
			case 29: s = "invalid TrueOrFalse"; break;
			case 30: s = "invalid SignalOrIsConnected"; break;
			case 31: s = "invalid SignalOrIsConnected"; break;
			case 32: s = "invalid SignalOrIsConnected"; break;
			case 33: s = "invalid SignalOp"; break;
			case 34: s = "invalid OrOp"; break;
			case 35: s = "invalid NotExp"; break;
			case 36: s = "invalid Value"; break;
			case 37: s = "invalid RelOp"; break;

			default: s = "error " + n; break;
		}
		errorStream.WriteLine(errMsgFormat, line, col, s);
		count++;
	}

	public virtual void SemErr (int line, int col, string s) {
		errorStream.WriteLine(errMsgFormat, line, col, s);
		count++;
	}
	
	public virtual void SemErr (string s) {
		errorStream.WriteLine(s);
		count++;
	}
	
	public virtual void Warning (int line, int col, string s) {
		errorStream.WriteLine(errMsgFormat, line, col, s);
	}
	
	public virtual void Warning(string s) {
		errorStream.WriteLine(s);
	}
} // Errors


public class FatalError: Exception {
	public FatalError(string m): base(m) {}
}
}