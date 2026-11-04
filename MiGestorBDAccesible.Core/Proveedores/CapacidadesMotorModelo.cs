using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Proveedores
{
    public class TerminosMotorModelo
    {
        public Func<long?, long?, string> Identidad { get; init; } = (semilla, incremento) => $"IDENTITY({semilla}, {incremento})";

        public bool TieneIndicesAgrupados { get; init; } = true;

        public bool TieneReglasAlActualizar { get; init; } = true;

        public string LlaveDeshabilitada { get; init; } = "NOCHECK";

        public string FraseLlaveDeshabilitada { get; init; } = "Esta llave está deshabilitada y no se comprueba";

        public string ColumnaCalculada { get; init; } = "AS";

        public string ColumnaCalculadaGuardada { get; init; } = "AS … PERSISTED";

        public string NombreIdentidad { get; init; } = "IDENTITY";

        public string EjemploTiposIdentidad { get; init; } = "int o bigint";

        public string EjemploPredeterminado { get; init; } = "0, 1, N'texto' o una función como GETDATE()";

        public string ComprobarExistentes { get; init; } = "WITH CHECK";

        public string SeparadorNombres { get; init; } = "";

        public string NombreId { get; init; } = "Id";

        public string TipoId { get; init; } = "int";

        public string TipoTexto { get; init; } = "nvarchar";

        public string ModificarColumna { get; init; } = "ALTER COLUMN";

        public bool RequiereEsquema { get; init; } = true;

        public bool IncrementoPorColumna { get; init; } = true;

        public Func<string, string, bool> TiposCompatibles { get; init; } = ReglaReferencialExtensiones.TiposCompatibles;
    }

    public class CapacidadesMotorModelo
    {
        public string ContenedoresTitulo { get; init; } = "Bases de datos";

        public string ContenedoresSistemaTitulo { get; init; } = "Bases de datos del sistema";

        public bool TieneEsquemas { get; init; } = true;

        public bool PuedeDeshabilitarDisparadores { get; init; } = true;

        public bool SalidasEnResultados { get; init; }

        public bool CheckPuedeOmitirComprobacion { get; init; } = true;

        public bool PuedeCrearContenedores { get; init; } = true;

        public bool PuedeDisenarTablas { get; init; } = true;

        public bool TienePapelera { get; init; }

        public bool SesionPorBase { get; init; }

        public bool DdlTransaccional { get; init; }

        public IReadOnlyList<TipoObjeto> TiposObjeto { get; init; } = [TipoObjeto.Vista, TipoObjeto.Procedimiento, TipoObjeto.Funcion, TipoObjeto.Secuencia];

        public string TituloDisparadoresBase { get; init; } = "Disparadores de la base";

        public bool LectoresEsperanCambios { get; init; }

        public bool IndiceIncluye { get; init; } = true;

        public bool IndiceFiltrado { get; init; } = true;

        public bool IndiceConcurrente { get; init; }

        public IReadOnlyList<(string Nombre, string Explicacion)> MetodosIndice { get; init; } = [];

        public IReadOnlyList<ReglaReferencial> ReglasAlEliminar { get; init; } = ReglasEstandar;

        public IReadOnlyList<ReglaReferencial> ReglasAlActualizar { get; init; } = ReglasEstandar;

        public static IReadOnlyList<ReglaReferencial> ReglasEstandar { get; } =
            [ReglaReferencial.SinAccion, ReglaReferencial.Cascada, ReglaReferencial.EstablecerNulo, ReglaReferencial.EstablecerPredeterminado];

        public TerminosMotorModelo Terminos { get; init; } = new();

        public int VersionPrincipalMinima { get; init; } = 14;

        public string NombreVersionMinima { get; init; } = "SQL Server 2017";

        public string? AvisoVersion(int versionPrincipal) =>
            versionPrincipal >= VersionPrincipalMinima
                ? null
                : $"Atención: esta aplicación funciona desde {NombreVersionMinima}. Este servidor es más antiguo, así que algunas funciones pueden fallar.";

        public static CapacidadesMotorModelo SqlServer { get; } = new() { LectoresEsperanCambios = true };
    }
}
