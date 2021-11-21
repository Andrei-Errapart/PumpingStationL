extendfqi(FQI1.EXTENDED, FQI1, FQI1.OFFSET);
extendfqi(FQI2.EXTENDED, FQI2, FQI2.OFFSET);
extendfqi(FQI3.EXTENDED, FQI3, FQI3.OFFSET);

Var Not_First_Round;
Var FQI1_So_Far;

# 1. MV11 loogika.
If MV11.AUTO_CONTROL Then
	# Kas reservid on tühjad? Avada kraanid...
	If LIA2.1 < LIA2.MIN Then
		MV11.CLOSE :=0;
		MV11.OPEN := 1;
	End;
	If MV11.IS_OPEN And (Not MV11.IS_CLOSED) Then
		MV11.OPEN := 0;
	End;

	# Kas on üle ääre minemas või on vaja pesta?
	# Hakata kraane sulgema, kui see on nii.
	If (LIA2.1 > LIA2.MAX) Or LSA2.1 Or FJP.P01.STOP Then
		MV11.OPEN := 0;
		MV11.CLOSE := 1;
	End;			
	If MV11.IS_CLOSED And (Not MV11.IS_OPEN) Then
		MV11.CLOSE := 0;
	End;
Else
	MV11.OPEN := 0;
	MV11.CLOSE := 0;
End;
	    
# 2. MV21 loogika.
If MV21.AUTO_CONTROL Then
	# Kas reservid on tühjad? Avada kraanid...
	If LIA2.2 < LIA2.MIN Then
		MV21.CLOSE := 0;
		MV21.OPEN := 1;
	End;
	If MV21.IS_OPEN And (Not MV21.IS_CLOSED) Then
		MV21.OPEN := 0;
	End;

	# Kas on üle ääre minemas või on tarvis pesta?
	# Hakata kraane sulgema, kui see on nii.
	If (LIA2.2 > LIA2.MAX) Or LSA2.2 Or FJP.P01.STOP Then
		MV21.OPEN := 0;
		MV21.CLOSE := 1;
	End;
	If MV21.IS_CLOSED And (Not MV21.IS_OPEN) Then
		MV21.CLOSE := 0;
	End;
Else
	MV21.OPEN := 0;
	MV21.CLOSE := 0;
End;

# Pesu on lubatud vaid siis, kui:
# 1. Pump on automaatjuhtimisel.
# 2. reservuaaride tase on üle miinimumi -- seda ei saa teha, sest praktiliselt iga pesu korral langeb alla miinimumi.
FJP.ENABLE_WASH := (P01.AUTO_CONTROL);
		
# 3. Kas pestakse?
If FJP.P01.STOP Then
	# PESUREZHIIM
	# Mootor seisma.
	P01.START_STOP := 0;
	DY1 := 0;
	 		
	# Käivitame P5 vaid siis kui
	# puurkaevupump seisab
	# ja klapid MV1 ja MV2 on kinni.
	P5.START_STOP := FJP.P5.START And (Not P01.IS_RUNNING) And MV11.IS_CLOSED And (Not MV11.IS_OPEN) And MV21.IS_CLOSED And (Not MV21.IS_OPEN);
Else
	# TAVALINE TÖÖPÄEV
	    	
	# 0. Ei mingit pesu.
	P5.START_STOP := 0;
	    	
	If P01.PLC_CONTROL Then
		If P01.AUTO_CONTROL Then
			# 2. Kas saame pumbata?
			If (MV11.IS_OPEN And (Not MV11.IS_CLOSED)) Or (MV21.IS_OPEN And (Not MV21.IS_CLOSED)) Then
				P01.START_STOP := 1;
			End;
			# 3. Kas saame pumba kinni keerata?
			If (MV11.IS_CLOSED Or MV11.CLOSE) And (MV21.IS_CLOSED Or MV21.CLOSE) Then
				P01.START_STOP := 0;
			End;
		End;
	Else
		P01.START_STOP := 0;
	End;

	# Aeratsiooniklapp vastavalt pumbale.
	DY1 := P01.START_STOP;

	# Saadame filtrisse edasi FQI1 impulsid.
	# FJP.LQI2 := BITS.FQI1;
	If Not_First_Round Then
		If FQI1 > FQI1_So_Far Then
			FQI1_So_Far := FQI1;
			FJP.LQI1 := 1;
		Else
			FJP.LQI1 := 0;
		End;
	Else
		Not_First_Round := 1;
		FQI1_So_Far := FQI1;
	End;
End;

# 4. II astme pumbapaterei juhtimine
# Kas väljundis on normaalne rõhk ja ühes või teises reservuaaris on piisavalt vett?
If PS.3 And ((LIA2.1 > LIA2.MIN) Or (LIA2.2 > LIA2.MIN)) Then
	PP1.START_STOP := 1;
	PP2.START_STOP := 1;
	PP3.START_STOP := 1;
	PP4.START_STOP := 1;
Else
	PP1.START_STOP := 0;
	PP2.START_STOP := 0;
	PP3.START_STOP := 0;
	PP4.START_STOP := 0;
End;

# FJP tahab nagunii teada, mis v2rk on.
FJP.P5_IS_RUNNING := P5.IS_RUNNING;


# 1. Toite alarm. Kontrollitakse pinge olekut kõigil kolmel faasil +-10% sees (>=360V AndAlso <=440V)
EQ.ALARM := (EQ1.L1L2 < 360) Or (EQ1.L1L2 > 440) Or (EQ1.L2L3 < 360) Or (EQ1.L2L3 > 440) Or (EQ1.L3L1 < 360) Or (EQ1.L3L1 > 440);

