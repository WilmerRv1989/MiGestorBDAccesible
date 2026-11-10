using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Edicion;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Proveedores;
using MiGestorBDAccesible.Core.Proveedores.PostgreSql;
using MiGestorBDAccesible.Core.Proveedores.SqlServer;
using Npgsql;

namespace MiGestorBDAccesible.Pruebas
{
    public class CierreSinPerdidaRegresion2Pruebas
    {
        [Fact]
        public async Task AC4_PreguntarYCancelarElCierreNoCancelaLaOperacion()
        {
            var conexion = new ConexionControlada();
            await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
            var operacion = sesion.CambiarAsync("INSERT INTO prueba VALUES (1)");
            await conexion.Entrada.Task;
            try
            {
                using (CierreAplicacion.BloquearOperaciones())
                {
                    Assert.False(conexion.Cancelada);
                    Assert.False(operacion.IsCompleted);
                }
                Assert.False(conexion.Cancelada);
            }
            finally
            {
                conexion.Continuar.TrySetResult();
                await operacion;
            }
        }

        [Fact]
        public async Task AC2_ConfirmarConOperacionEnCursoNoCancelaNiFinalizaNingunaSesion()
        {
            var conexion = new ConexionControlada();
            await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
            var proveedor = new FinalizadorControlado();
            var operacion = sesion.CambiarAsync("INSERT INTO prueba VALUES (1)");
            await conexion.Entrada.Task;
            Task<ResultadoCierreModelo>? cierre = null;
            try
            {
                cierre = CierreAplicacion.TerminarAsync([proveedor], true);
                Assert.True(cierre.IsCompleted);
                var resultado = await cierre;
                Assert.False(resultado.Correcto);
                Assert.Contains(resultado.Errores, error => error.Contains("ejecución", StringComparison.OrdinalIgnoreCase));
                Assert.Equal(0, proveedor.Confirmaciones);
                Assert.False(conexion.Cancelada);
            }
            finally
            {
                conexion.Continuar.TrySetResult();
                await operacion;
                if (cierre is not null) await cierre;
            }
        }

        [Fact]
        public async Task AC1_ConfirmarFilasConOperacionEnCursoNoLaCancela()
        {
            var conexion = new ConexionControlada();
            await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
            var operacion = sesion.CambiarAsync("INSERT INTO prueba VALUES (1)");
            await conexion.Entrada.Task;
            try
            {
                Assert.NotNull(await sesion.ConfirmarAsync());
                Assert.False(conexion.Cancelada);
                Assert.Equal(0, conexion.Transaccion.Confirmaciones);
            }
            finally
            {
                conexion.Continuar.TrySetResult();
                await operacion;
            }
        }

        [Theory]
        [InlineData(TipoMotor.PostgreSql)]
        [InlineData(TipoMotor.SqlServer)]
        public async Task AC6_UnCierreEnOtraEjecucionNoContaminaLaDivisionDelScript(TipoMotor motor)
        {
            var entrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task otroCierre;
            using (ExecutionContext.SuppressFlow())
            otroCierre = Task.Run(async () =>
            {
                using var bloqueo = CierreAplicacion.BloquearOperaciones();
                entrada.TrySetResult();
                await continuar.Task;
            });
            await entrada.Task;
            try
            {
                IProveedorBaseDatos proveedor = motor == TipoMotor.PostgreSql ? new ProveedorPostgreSql() : new ProveedorSqlServer();
                var conexion = new ConexionActiva(new ConexionModelo { Motor = motor, Servidor = "127.0.0.1", Puerto = 1 }, null, new ResultadoPruebaConexion(true, TimeSpan.Zero));
                var resultado = await proveedor.EjecutarAsync(conexion, null, motor == TipoMotor.PostgreSql ? "SELECT $sin_cerrar$" : "GO 99999999999", 12);
                Assert.Equal(12, Assert.Single(resultado.Mensajes).Linea);
            }
            finally
            {
                continuar.TrySetResult();
                await otroCierre;
            }
        }

        [Fact]
        public async Task AC1_UnaLecturaCon25P02SoloPermiteDeshacerSinOfrecerReintento()
        {
            var conexion = new ConexionControlada { AbortarLectura = true };
            conexion.Continuar.TrySetResult();
            await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
            Assert.True((await sesion.CambiarAsync("INSERT INTO prueba VALUES (1)")).Correcto);
            await Assert.ThrowsAsync<InvalidOperationException>(() => sesion.LeerAsync("SELECT 1 / 0"));

            var resultado = await CierreAplicacion.TerminarAsync([], true);

            Assert.False(resultado.Correcto);
            Assert.False(CierreAplicacion.OfreceReintentar(resultado, true));
            Assert.Contains(resultado.Errores, error => error.Contains("no se pueden guardar") && error.Contains("ROLLBACK"));
            Assert.DoesNotContain(resultado.Errores, error => error.Contains("perdió la conexión"));
            Assert.Equal(0, conexion.Transaccion.Confirmaciones);
            Assert.Null(await sesion.DeshacerAsync());
        }

        [Fact]
        public async Task AC6_ElRegistroDeFilasNoSeComparteEntreEjecucionesIndependientes()
        {
            var entrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var continuar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task otraEdicion;
            using (ExecutionContext.SuppressFlow())
            otraEdicion = Task.Run(async () =>
            {
                var conexion = new ConexionControlada();
                conexion.Continuar.TrySetResult();
                await using var sesion = new SesionEdicionAdo(conexion, ex => ex.Message);
                await sesion.CambiarAsync("INSERT INTO prueba VALUES (1)");
                entrada.TrySetResult();
                await continuar.Task;
                Assert.True(RegistroSesionesEdicion.HayCambiosPendientes);
            });
            await entrada.Task;
            try
            {
                Assert.False(RegistroSesionesEdicion.HayCambiosPendientes);
            }
            finally
            {
                continuar.TrySetResult();
                await otraEdicion;
            }
        }

        private sealed class FinalizadorControlado : IFinalizadorSesiones
        {
            public TipoMotor Motor => TipoMotor.MySql;
            public int Confirmaciones { get; private set; }
            public Task<ResultadoFinalizacionModelo> FinalizarSesionAsync(ConexionActiva? conexion, bool confirmar)
            {
                if (confirmar) Confirmaciones++;
                return Task.FromResult(new ResultadoFinalizacionModelo());
            }
        }

        private sealed class ConexionControlada : DbConnection
        {
            [AllowNull] public override string ConnectionString { get; set; } = "";
            public override string Database => "Prueba";
            public override string DataSource => "Memoria";
            public override string ServerVersion => "1";
            public override ConnectionState State => ConnectionState.Open;
            public TaskCompletionSource Entrada { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Continuar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool AbortarLectura { get; init; }
            public CancellationToken Cancelacion { get; set; }
            public bool Cancelada => Cancelacion.IsCancellationRequested;
            public TransaccionControlada Transaccion { get; private set; } = null!;
            public override void Open() { }
            public override void Close() { }
            public override void ChangeDatabase(string nombre) => throw new NotSupportedException();
            protected override DbTransaction BeginDbTransaction(IsolationLevel aislamiento) => Transaccion = new(this);
            protected override DbCommand CreateDbCommand() => new ComandoControlado(this);
        }

        private sealed class TransaccionControlada(ConexionControlada conexion) : DbTransaction
        {
            public int Confirmaciones { get; private set; }
            public bool Abortada { get; set; }
            protected override DbConnection DbConnection => conexion;
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public override void Save(string nombre)
            {
                if (Abortada) throw new PostgresException("Transacción abortada", "ERROR", "ERROR", "25P02");
            }
            public override void Commit() => Confirmaciones++;
            public override void Rollback() { }
            public override void Rollback(string nombre) { }
        }

        private sealed class ComandoControlado(ConexionControlada conexion) : DbCommand
        {
            [AllowNull] public override string CommandText { get; set; } = "";
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; }
            public override bool DesignTimeVisible { get; set; }
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; } = conexion;
            protected override DbTransaction? DbTransaction { get; set; }
            protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
            public override void Cancel() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancelacion)
            {
                conexion.Cancelacion = cancelacion;
                conexion.Entrada.TrySetResult();
                await conexion.Continuar.Task;
                return 1;
            }
            public override object? ExecuteScalar() => throw new NotSupportedException();
            public override void Prepare() => throw new NotSupportedException();
            protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior comportamiento)
            {
                if (conexion.AbortarLectura)
                {
                    conexion.Transaccion.Abortada = true;
                    throw new PostgresException("Transacción abortada", "ERROR", "ERROR", "25P02");
                }
                throw new NotSupportedException();
            }
        }
    }
}
