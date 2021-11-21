package com.errapartengineering.plcengine;

public class ApplicationException extends Exception {

	public ApplicationException() {
	}

	public ApplicationException(String detailMessage) {
		super(detailMessage);
	}

	public ApplicationException(Throwable throwable) {
		super(throwable);
	}

	public ApplicationException(String detailMessage, Throwable throwable) {
		super(detailMessage, throwable);
	}

	// shut up the compiler.
	static final long serialVersionUID = 0x3075A63E30F03244L;
}
