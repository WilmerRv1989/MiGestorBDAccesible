using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Definicion
{
    public enum TipoCambioTabla
    {
        QuitarLlaveForanea,
        QuitarUnica,
        QuitarClavePrincipal,
        QuitarColumna,
        RenombrarColumna,
        CambiarTipo,
        CambiarNulos,
        CambiarPredeterminado,
        CambiarIdentidad,
        AgregarColumna,
        CrearClavePrincipal,
        AgregarUnica,
        AgregarLlaveForanea,
        RenombrarTabla
    }

    public class CambioTablaModelo
    {
        public TipoCambioTabla Tipo { get; init; }

        public DefinicionColumnaModelo? Anterior { get; init; }

        public DefinicionColumnaModelo? Nueva { get; init; }

        public string? Restriccion { get; init; }

        public List<string> Columnas { get; init; } = [];

        public string Frase { get; set; } = "";

        public string NombreColumna => Nueva?.Nombre ?? Anterior?.Nombre ?? "";
    }

    public class PlanModificacionModelo
    {
        public DetalleTablaModelo Original { get; init; } = new();

        public DefinicionTablaModelo Nueva { get; init; } = new();

        public List<CambioTablaModelo> Cambios { get; init; } = [];

        public List<string> MotivosRecreacion { get; init; } = [];

        public bool HayCambios => Cambios.Count > 0 || MotivosRecreacion.Count > 0;
    }

    public class SentenciaCambioModelo
    {
        public string Sql { get; init; } = "";

        public string Frase { get; init; } = "";
    }

    public class ScriptModificacionModelo
    {
        public List<SentenciaCambioModelo> Sentencias { get; init; } = [];

        public List<string> NoAplicables { get; init; } = [];

        public string Texto { get; init; } = "";
    }

    public class ComprobacionModelo
    {
        public string Mensaje { get; init; } = "";

        public string? ConsultaFilas { get; init; }

        public bool Bloquea { get; init; }
    }

    public class ResultadoModificacionModelo
    {
        public List<string> Aplicados { get; init; } = [];

        public string? Error { get; init; }

        public string? FraseFallida { get; init; }

        public bool Correcto => Error is null;
    }
}
