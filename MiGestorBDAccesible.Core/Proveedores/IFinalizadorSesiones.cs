using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Proveedores
{
    public interface IFinalizadorSesiones
    {
        TipoMotor Motor { get; }

        bool TieneCambiosPendientes(ConexionActiva? conexion = null);

        Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar);

        Task DescartarSesionesAsync(ConexionActiva? conexion);
    }
}
