extendfqi(FQI1.EXTENDED, FQI1, FQI1.OFFSET);
extendfqi(FQI2.EXTENDED, FQI2, FQI2.OFFSET);
extendfqi(FQI3.EXTENDED, FQI3, FQI3.OFFSET);
If FJP.WASHING Then
	P01.START_STOP := 0;
	M1.ALARM := 0;
	M2.ALARM := 0;
	M3.ALARM := 0;
	M4.ALARM := 0;
	M5.ALARM := 0;
	M6.ALARM := 0;
	M7.ALARM := 0;
	M8.ALARM := 0;
Else
	If P01.PLC_CONTROL And (Not P01.ERROR) And (MV11.AUTO_CONTROL Or MV21.AUTO_CONTROL) Then
		# 1. MV11 loogika.
		If MV11.AUTO_CONTROL Then
			# Kas reservid on tühjad? Avada kraanid...
			If LIA2.1 < LIA2.MIN Then
				MV11.OPEN := 1;
				MV11.CLOSE :=0;
			End;
			# Kas on üle ääre minemas või on vaja pesta?
			# Hakata kraane sulgema, kui see on nii.
			If (LIA2.1 > LIA2.MAX) Or LSA2.1 Then
				MV11.OPEN := 0;
				MV11.CLOSE := 1;
			End;			
		Else
			MV11.OPEN := 0;
			MV11.CLOSE := 0;
		End;
		    
		# 2. MV21 loogika.
		If MV21.AUTO_CONTROL Then
			# Kas reservid on tühjad? Avada kraanid...
			If LIA2.2 < LIA2.MIN Then
				MV21.OPEN := 1;
				MV21.CLOSE := 0;
			End;

			# Kas on üle ääre minemas või on tarvis pesta?
			# Hakata kraane sulgema, kui see on nii.
			If (LIA2.2 > LIA2.MAX) Or LSA2.2 Then
				MV21.OPEN := 0;
				MV21.CLOSE := 1;
			End;
		Else
			MV21.OPEN := 0;
			MV21.CLOSE := 0;
		End;
		P01.START_STOP := MV11.IS_OPEN Or MV21.IS_OPEN;

	Else
		# Pump rikkis või juhtimine keelatud.
		P01.START_STOP := 0;
		MV11.OPEN := 0;
		MV11.CLOSE := 1;
		MV21.OPEN := 0;
		MV21.CLOSE := 1;
	End;

	# Pesu on lubatud vaid siis, kui:
	# 1. Pump on automaatjuhtimisel.
	# 2. reservuaaride tase on üle miinimumi
	FJP.ENABLE_WASH := ((LIA2.2 > LIA2.MIN) Or (LIA2.1 > LIA2.MIN)) And P5.AUTO_CONTROL;

	M1.ALARM := Not M1.FB;
	M2.ALARM := Not M2.FB;
	M3.ALARM := Not M3.FB;
	M4.ALARM := Not M4.FB;
	M5.ALARM := Not M5.FB;
	M6.ALARM := Not M6.FB;
	M7.ALARM := Not M7.FB;
	M8.ALARM := Not M8.FB;
End;

# Dosaatori käivitamine.
DP11.START_STOP := P01.IS_RUNNING;

# 2 astme pumbad peavad alati käima, v.a. 1.
# PP1.START_STOP := 1;
PP2.START_STOP := 1;
PP3.START_STOP := 1;
PP4.START_STOP := 1;

