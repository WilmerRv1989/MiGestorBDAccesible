using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public class ProveedorMySql : ProveedorMySqlBase
    {
        private static readonly CapacidadesMotorModelo _capacidades = CrearCapacidades(800, "MySQL 8.0", "STORED", [TipoObjeto.Vista, TipoObjeto.Procedimiento, TipoObjeto.Funcion]);

        public override TipoMotor Motor => TipoMotor.MySql;

        public override CapacidadesMotorModelo Capacidades => _capacidades;

        protected override string ConsultaRestricciones => """
            SELECT tc.constraint_name, cc.check_clause
            FROM information_schema.table_constraints tc
            JOIN information_schema.check_constraints cc
              ON cc.constraint_schema = tc.constraint_schema AND cc.constraint_name = tc.constraint_name
            WHERE tc.table_schema = @base AND tc.table_name = @tabla AND tc.constraint_type = 'CHECK'
            ORDER BY tc.constraint_name
            """;

        protected override bool EsEsteMotor(string version) => !version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
    }
}
