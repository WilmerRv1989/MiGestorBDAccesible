using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Proveedores
{
    public interface IFinalizadorSesiones
    {
        TipoMotor Motor { get; }

        bool TieneCambiosPendientes(ConexionActiva? conexion = null) => false;

        Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar) =>
            Task.FromResult(new ResultadoFinalizacionModelo());

        Task DescartarSesionesAsync(ConexionActiva? conexion) => Task.CompletedTask;
    }
}
