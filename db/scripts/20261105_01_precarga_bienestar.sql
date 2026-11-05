-- CajaAhorro / 20261105_01_precarga_bienestar.sql
-- SOLO DESARROLLO. No ejecutar en producción sin respaldo, autorización
-- y plan revisado de reversión. No toca tablas financieras existentes.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.AhorroBienestarCargas', N'U') IS NOT NULL
        THROW 51001, 'AhorroBienestarCargas ya existe. Detener y revisar versión del esquema.', 1;
    IF OBJECT_ID(N'dbo.AhorroBienestarCargaLineas', N'U') IS NOT NULL
        THROW 51002, 'AhorroBienestarCargaLineas ya existe. Detener y revisar versión del esquema.', 1;

    CREATE TABLE dbo.AhorroBienestarCargas (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AhorroBienestarCargas PRIMARY KEY,
        NombreArchivo nvarchar(255) NOT NULL,
        Sha256 nvarchar(64) NOT NULL,
        Hoja nvarchar(100) NOT NULL,
        UsuarioCarga nvarchar(150) NOT NULL,
        FechaCargaUtc datetime2 NOT NULL,
        Estado nvarchar(24) NOT NULL CONSTRAINT DF_AhorroBienestarCargas_Estado DEFAULT N'Precarga',
        RegistrosValidosAhorro int NOT NULL,
        RegistrosOtrosConceptos int NOT NULL,
        RegistrosConIncidencia int NOT NULL,
        ImporteAhorro decimal(18,2) NOT NULL
    );
    CREATE UNIQUE INDEX IX_AhorroBienestarCargas_Sha256
        ON dbo.AhorroBienestarCargas (Sha256);

    CREATE TABLE dbo.AhorroBienestarCargaLineas (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AhorroBienestarCargaLineas PRIMARY KEY,
        CargaId bigint NOT NULL,
        FilaExcel int NOT NULL,
        RFC nvarchar(13) NOT NULL,
        CURP nvarchar(18) NULL,
        Nombre nvarchar(200) NOT NULL,
        Plaza nvarchar(80) NOT NULL,
        Concepto nvarchar(10) NOT NULL,
        Quincena int NULL,
        Anio int NULL,
        FechaPago datetime2 NULL,
        Importe decimal(18,2) NULL,
        EstatusPago nvarchar(40) NOT NULL,
        EstadoValidacion nvarchar(24) NOT NULL,
        Motivo nvarchar(300) NULL,
        CONSTRAINT FK_AhorroBienestarCargaLineas_AhorroBienestarCargas_CargaId
            FOREIGN KEY (CargaId) REFERENCES dbo.AhorroBienestarCargas(Id)
            ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX IX_AhorroBienestarCargaLineas_CargaId_FilaExcel
        ON dbo.AhorroBienestarCargaLineas(CargaId, FilaExcel);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
