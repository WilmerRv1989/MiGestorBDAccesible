namespace MiGestorBDAccesible.Core.Estructura
{
    public enum TipoObjeto
    {
        Vista,
        Procedimiento,
        Funcion,
        Paquete,
        Secuencia,
        Disparador
    }

    public enum DireccionParametro
    {
        Entrada,
        Salida,
        EntradaSalida
    }

    public class ObjetoBaseDatosModelo
    {
        public TipoObjeto Tipo { get; init; }

        public string Esquema { get; init; } = "";

        public string Nombre { get; init; } = "";

        public string? Firma { get; init; }

        public TablaModelo? Tabla { get; init; }

        public bool Valido { get; init; } = true;

        public bool Materializada { get; init; }

        public long? Id { get; init; }

        public string NombreCompleto => Esquema == "" ? Nombre : $"{Esquema}.{Nombre}";

        public string Texto => $"{NombreCompleto}{(Firma is null ? "" : $"({Firma})")}{(Valido ? "" : " (inválido)")}";
    }

    public class ParametroModelo
    {
        public string Nombre { get; init; } = "";

        public string TipoDatos { get; init; } = "";

        public DireccionParametro Direccion { get; init; }
    }

    public class ErrorCompilacionModelo
    {
        public int Linea { get; init; }

        public int Columna { get; init; }

        public string Texto { get; init; } = "";
    }

    public class DetalleObjetoModelo
    {
        public ObjetoBaseDatosModelo Objeto { get; init; } = new();

        public List<ColumnaModelo> Columnas { get; init; } = [];

        public List<ParametroModelo> Parametros { get; init; } = [];

        public string? Retorno { get; init; }

        public bool DevuelveTabla { get; init; }

        public List<string> Subprogramas { get; init; } = [];

        public List<ErrorCompilacionModelo> Errores { get; init; } = [];

        public string? Momento { get; init; }

        public List<string> Eventos { get; init; } = [];

        public bool? PorFila { get; init; }

        public bool? Habilitado { get; init; }

        public string? FuncionDisparador { get; init; }

        public string? ValorActual { get; init; }

        public string? Inicio { get; init; }

        public string? Incremento { get; init; }

        public string? Minimo { get; init; }

        public string? Maximo { get; init; }

        public bool? Ciclo { get; init; }

        public List<string> Etiquetas { get; init; } = [];

        public List<string> Notas { get; init; } = [];
    }
}
