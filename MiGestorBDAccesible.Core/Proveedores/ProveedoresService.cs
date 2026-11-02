using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Proveedores.Oracle;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;

namespace MiGestorBDAccesible.Core.Proveedores
{
    public class ProveedoresService
    {
        private readonly Dictionary<TipoMotor, IProveedorBaseDatos> _proveedores = new()
        {
            [TipoMotor.SqlServer] = new ProveedorSqlServer(),
            [TipoMotor.Oracle] = new ProveedorOracle(),
            [TipoMotor.PostgreSql] = new ProveedorPostgreSql()
        };

        public IEnumerable<IProveedorBaseDatos> Todos => _proveedores.Values;

        public bool EstaDisponible(TipoMotor motor) => _proveedores.ContainsKey(motor);

        public IProveedorBaseDatos Obtener(TipoMotor motor)
        {
            if (_proveedores.TryGetValue(motor, out var proveedor))
            {
                return proveedor;
            }

            throw new NotSupportedException($"{motor.Nombre()} todavía no está disponible en esta versión.");
        }
    }
}
