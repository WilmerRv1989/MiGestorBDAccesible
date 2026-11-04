namespace MiGestorBDAccesible.Core.Conexiones
{
    public enum TipoMotor
    {
        SqlServer,
        Oracle,
        PostgreSql,
        MySql,
        MariaDb
    }

    public static class TipoMotorExtensiones
    {
        public static IReadOnlyList<TipoMotor> EnOrdenAlfabetico { get; } =
            Enum.GetValues<TipoMotor>().OrderBy(m => m.Nombre(), StringComparer.OrdinalIgnoreCase).ToList();

        public static string Nombre(this TipoMotor motor) => motor switch
        {
            TipoMotor.SqlServer => "SQL Server",
            TipoMotor.Oracle => "Oracle",
            TipoMotor.PostgreSql => "PostgreSQL",
            TipoMotor.MySql => "MySQL",
            TipoMotor.MariaDb => "MariaDB",
            _ => motor.ToString()
        };

        public static int PuertoPredeterminado(this TipoMotor motor) => motor switch
        {
            TipoMotor.SqlServer => 1433,
            TipoMotor.Oracle => 1521,
            TipoMotor.PostgreSql => 5432,
            _ => 3306
        };

        public static bool AdmiteAutenticacionWindows(this TipoMotor motor) =>
            motor == TipoMotor.SqlServer && OperatingSystem.IsWindows();

        public static bool UsaNombreServicio(this TipoMotor motor) => motor == TipoMotor.Oracle;

        public static string? ServicioPredeterminado(this TipoMotor motor) => motor == TipoMotor.Oracle ? "FREEPDB1" : null;

        public static bool AdmiteBaseInicial(this TipoMotor motor) => motor is TipoMotor.SqlServer or TipoMotor.PostgreSql or TipoMotor.MySql or TipoMotor.MariaDb;

        public static string? BaseInicialPredeterminada(this TipoMotor motor) => motor == TipoMotor.PostgreSql ? "postgres" : null;

        public static string AyudaBaseInicial(this TipoMotor motor) => motor switch
        {
            TipoMotor.PostgreSql => "PostgreSQL siempre se conecta a una base; desde ella se ven las demás. Si la dejas vacía se usa postgres.",
            TipoMotor.MySql or TipoMotor.MariaDb => "Si la dejas vacía te conectas al servidor sin elegir base, y eliges la base en el explorador.",
            _ => "Si la dejas vacía se usa la base predeterminada del usuario."
        };
    }
}
