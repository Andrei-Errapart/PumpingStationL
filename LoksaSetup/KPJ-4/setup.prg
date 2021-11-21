Var Level1;
Var Level2;
Var Level3;
Var P1ok;
Var P2ok;

Level1 := 1000;
Level2 := 1150;
Level3 := 1200;

P1ok := P1.AUTO And (Not P1.ALARM);
P2ok := P2.AUTO And (Not P2.ALARM);
ALARM.WATER_LOW := LIA1 < 900;
ALARM.WATER_HIGH := LIA1 > 1400;

If LIA1 < Level1 Then
	P1.START_STOP := 0;
	P2.START_STOP := 0;
Else
	If LIA1 > Level3 Then
		P1.START_STOP := 1;
		P2.START_STOP := 1;
	End;
	If (LIA1 > Level2) And ((Not P1.START_STOP) Or (Not P1ok)) And ((Not P2.START_STOP) Or (Not P2ok)) Then
		If RUNFIRST Then
			P1.START_STOP := 1;
			RUNFIRST := 0;
		Else
			P2.START_STOP := 1;
			RUNFIRST := 1;
		End;
	End;
End;

# P1 - kas jätta seisma?
If Not P1ok Then
	P1.START_STOP := 0;
End;

# P2 - kas jätta seisma?
If Not P2ok Then
	P2.START_STOP := 0;
End;
