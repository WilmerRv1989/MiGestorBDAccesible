using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;
using MiGestorBDAccesible.Core.Edicion;
using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        public ContextoCierre Contexto { get; } = ContextoCierre.Actual;

        private const int FilasMostradasMaximas = 1000;

        public async Task<ResultadoEjecucionModelo> EjecutarAsync(
            ConexionActiva conexion,
            string? baseDatos,
            string script,
            int lineaInicial,
            CancellationToken cancelacion = default)
        {
            var resultado = new ResultadoEjecucionModelo();
            using var operacion = Contexto.Operaciones.Iniciar(cancelacion);
            if (operacion is null)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = OperacionesBaseDatos.MensajeCierre });
                return resultado;
            }
            cancelacion = operacion.Cancelacion;
            var cronometro = Stopwatch.StartNew();
            List<LoteModelo> lotes;

            try
            {
                lotes = DivisorLotes.Dividir(script);
            }
            catch (ScriptNoDivisibleException ex)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = ex.Message,
                    Linea = lineaInicial + ex.Linea - 1
                });
                resultado.BaseDatosFinal = baseDatos;
                resultado.Duracion = cronometro.Elapsed;
                return resultado;
            }

            LoteModelo? loteActual = null;

            int? LineaAbsoluta(int lineaEnLote) =>
                loteActual is null || lineaEnLote <= 0
                    ? null
                    : lineaInicial + loteActual.LineaInicial - 1 + lineaEnLote - 1;

            SqlConnection conexionSql;

            try
            {
                conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                resultado.Cancelado = true;
                return resultado;
            }
            catch (ConexionPerdidaException ex)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = ex.Message });
                resultado.Duracion = cronometro.Elapsed;
                return resultado;
            }
            catch (SqlException ex)
            {
                resultado.Mensajes.Add(new MensajeEjecucionModelo { Tipo = TipoMensaje.Error, Texto = ex.Message, Numero = ex.Number });
                resultado.Duracion = cronometro.Elapsed;
                return resultado;
            }

            await using (conexionSql)
            {
                conexionSql.InfoMessage += (_, e) =>
                {
                    foreach (SqlError error in e.Errors)
                    {
                        resultado.Mensajes.Add(new MensajeEjecucionModelo
                        {
                            Tipo = TipoMensaje.Informacion,
                            Texto = error.Message,
                            Numero = error.Number == 0 ? null : error.Number,
                            Linea = error.Number == 0 ? null : LineaAbsoluta(error.LineNumber)
                        });
                    }
                };

                foreach (var lote in lotes)
                {
                    loteActual = lote;

                    for (var repeticion = 0; repeticion < lote.Repeticiones && !resultado.Cancelado; repeticion++)
                    {
                        await EjecutarLoteAsync(conexionSql, lote, resultado, LineaAbsoluta, cancelacion);
                    }

                    if (resultado.Cancelado)
                    {
                        break;
                    }
                }

                resultado.BaseDatosFinal = conexionSql.State == System.Data.ConnectionState.Open ? conexionSql.Database : baseDatos;
            }

            resultado.Duracion = cronometro.Elapsed;
            return resultado;
        }

        private static async Task EjecutarLoteAsync(
            SqlConnection conexionSql,
            LoteModelo lote,
            ResultadoEjecucionModelo resultado,
            Func<int, int?> lineaAbsoluta,
            CancellationToken cancelacion)
        {
            await using var comando = new SqlCommand(lote.Texto, conexionSql) { CommandTimeout = 0 };

            comando.StatementCompleted += (_, e) =>
            {
                resultado.FilasAfectadas += e.RecordCount;
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.FilasAfectadas,
                    Texto = e.RecordCount == 1 ? "1 fila afectada." : $"{e.RecordCount:N0} filas afectadas."
                });
            };

            try
            {
                await using var lector = await comando.ExecuteReaderAsync(cancelacion);

                do
                {
                    if (lector.FieldCount > 0)
                    {
                        resultado.Conjuntos.Add(await LeerConjuntoAsync(lector, cancelacion));
                    }
                }
                while (await lector.NextResultAsync(cancelacion));
            }
            catch (SqlException) when (cancelacion.IsCancellationRequested)
            {
                MarcarCancelado(resultado);
            }
            catch (OperationCanceledException)
            {
                MarcarCancelado(resultado);
            }
            catch (SqlException ex) when (EsErrorDeConexion(ex))
            {
                SqlConnection.ClearAllPools();
                resultado.Cancelado = true;
                resultado.Mensajes.Add(new MensajeEjecucionModelo
                {
                    Tipo = TipoMensaje.Error,
                    Texto = $"Se perdió la conexión con el servidor durante la ejecución. Comprueba que siga encendido y vuelve a ejecutar. Detalle: {ex.Message}"
                });
            }
            catch (SqlException ex)
            {
                foreach (SqlError error in ex.Errors)
                {
                    resultado.Mensajes.Add(new MensajeEjecucionModelo
                    {
                        Tipo = error.Class > 10 ? TipoMensaje.Error : TipoMensaje.Informacion,
                        Texto = error.Message,
                        Numero = error.Number,
                        Linea = lineaAbsoluta(error.LineNumber)
                    });
                }
            }
        }

        private static void MarcarCancelado(ResultadoEjecucionModelo resultado)
        {
            resultado.Cancelado = true;
            resultado.Mensajes.Add(new MensajeEjecucionModelo
            {
                Tipo = TipoMensaje.Informacion,
                Texto = "La ejecución se canceló."
            });
        }

        private static async Task<ConjuntoResultadosModelo> LeerConjuntoAsync(SqlDataReader lector, CancellationToken cancelacion)
        {
            var conjunto = new ConjuntoResultadosModelo();
            var tipos = new string[lector.FieldCount];

            for (var i = 0; i < lector.FieldCount; i++)
            {
                tipos[i] = lector.GetDataTypeName(i);
                var nombre = lector.GetName(i);

                conjunto.Columnas.Add(new ColumnaResultadoModelo
                {
                    Nombre = string.IsNullOrEmpty(nombre) ? "(Sin nombre de columna)" : nombre,
                    TipoDatos = tipos[i]
                });
            }

            while (await lector.ReadAsync(cancelacion))
            {
                conjunto.TotalFilas++;

                if (conjunto.Filas.Count >= FilasMostradasMaximas)
                {
                    continue;
                }

                var fila = new string?[lector.FieldCount];

                for (var i = 0; i < lector.FieldCount; i++)
                {
                    fila[i] = lector.IsDBNull(i) ? null : Formatear(lector.GetValue(i), tipos[i]);
                }

                conjunto.Filas.Add(fila);
            }

            return conjunto;
        }

        private static string Formatear(object valor, string tipo) => valor switch
        {
            DateTime fecha when tipo == "date" => fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            DateTimeOffset fecha => fecha.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture),
            TimeSpan hora => hora.ToString(@"hh\:mm\:ss\.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.'),
            bool logico => logico ? "1" : "0",
            byte[] bytes => "0x" + Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 32)) + (bytes.Length > 32 ? "…" : ""),
            IFormattable formateable => formateable.ToString(null, CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? ""
        };
    }
}
