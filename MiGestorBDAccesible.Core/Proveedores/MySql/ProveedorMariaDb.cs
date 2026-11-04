using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.MySql
{
    public class ProveedorMariaDb : ProveedorMySqlBase
    {
        private static readonly CapacidadesMotorModelo _capacidades = CrearCapacidades(1006, "MariaDB 10.6", "PERSISTENT", [TipoObjeto.Vista, TipoObjeto.Procedimiento, TipoObjeto.Funcion, TipoObjeto.Secuencia]);

        public override TipoMotor Motor => TipoMotor.MariaDb;

        public override CapacidadesMotorModelo Capacidades => _capacidades;

        protected override string ConsultaRestricciones => """
            SELECT constraint_name, check_clause
            FROM information_schema.check_constraints
            WHERE constraint_schema = @base AND table_name = @tabla
            ORDER BY constraint_name
            """;

        protected override void EscribirFinInsertar(StringBuilder script, DetalleTablaModelo detalle)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];

            if (clave.Count == 0)
            {
                script.AppendLine(");");
                return;
            }

            script.AppendLine(")");
            script.AppendLine($"RETURNING {string.Join(", ", clave.Select(Identificador))};  -- Devuelve la clave de la fila nueva.");
        }

        protected override bool EsEsteMotor(string version) => version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
    }
}
