ALTER TABLE "SignalValue" RENAME TO "_SignalValue_old";

CREATE TABLE "SignalValue" ( 
    "Id"              INTEGER            PRIMARY KEY AUTOINCREMENT
                                       NOT NULL,
    "CreateDate"      DATETIME           NOT NULL,
    "SessionId"       INTEGER            NOT NULL,
    "OriginalId"      INTEGER            NOT NULL,
    "TimeStamp"       DATETIME           NOT NULL,
    "ConfigurationId" INTEGER            NOT NULL,
    "SignalValues"    VARBINARY( 2048 )  NOT NULL,
    CONSTRAINT "FK_SignalValue_SessionId" FOREIGN KEY ( "SessionId" ) REFERENCES "Session" ( "Id" ),
    CONSTRAINT "FK_SignalValue_ConfigurationId" FOREIGN KEY ( "ConfigurationId" ) REFERENCES "Configuration" ( "Id" ),
    CONSTRAINT "Unique_OriginalId_Timestamp_ConfigurationId" UNIQUE ( "OriginalId" ASC, "TimeStamp" ASC, "ConfigurationId" ASC ) ON CONFLICT FAIL 
);


INSERT INTO "SignalValue" SELECT * FROM "_SignalValue_old";
DROP TABLE "_SignalValue_old";
