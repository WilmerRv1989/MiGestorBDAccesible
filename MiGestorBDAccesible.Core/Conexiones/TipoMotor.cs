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

        public static bool AdmiteAutenticacionWindows(this TipoMotor motor) => motor == TipoMotor.SqlServer;

        public static bool UsaNombreServicio(this TipoMotor motor) => motor == TipoMotor.Oracle;

        public static string? ServicioPredeterminado(this TipoMotor motor) => motor == TipoMotor.Oracle ? "FREEPDB1" : null;

        public static bool AdmiteBaseInicial(this TipoMotor motor) => motor is TipoMotor.SqlServer or TipoMotor.PostgreSql;

        public static string? BaseInicialPredeterminada(this TipoMotor motor) => motor == TipoMotor.PostgreSql ? "postgres" : null;
    }
}
