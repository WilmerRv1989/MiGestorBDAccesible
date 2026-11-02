using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;
using Microsoft.Data.SqlClient;
using System.Text;

namespace MiGestorBDAccesible.Core.Proveedores.SqlServer
{
    public partial class ProveedorSqlServer
    {
        private const string ConsultaDestinosLlave = """
            SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale, i.is_primary_key
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE (i.is_primary_key = 1 OR i.is_unique_constraint = 1)
              AND t.is_ms_shipped = 0
              AND (SELECT COUNT(*) FROM sys.index_columns x
                   WHERE x.object_id = i.object_id AND x.index_id = i.index_id AND x.key_ordinal > 0) = 1
            ORDER BY s.name, t.name, i.is_primary_key DESC, c.name
            """;

        public async Task<List<DestinoLlaveModelo>> ObtenerDestinosLlaveAsync(
            ConexionActiva conexion,
            string baseDatos,
            CancellationToken cancelacion = default)
        {
            await using var conexionSql = await AbrirAsync(conexion, baseDatos, cancelacion);
            await using var comando = new SqlCommand(ConsultaDestinosLlave, conexionSql);
            await using var lector = await comando.ExecuteReaderAsync(cancelacion);

            var destinos = new List<DestinoLlaveModelo>();

            while (await lector.ReadAsync(cancelacion))
            {
                destinos.Add(new DestinoLlaveModelo
                {
                    Tabla = new TablaModelo { Esquema = lector.GetString(0), Nombre = lector.GetString(1) },
                    Columna = lector.GetString(2),
                    TipoDatos = FormatearTipo(lector.GetString(3), lector.GetInt16(4), lector.GetByte(5), lector.GetByte(6)),
                    EsClavePrincipal = lector.GetBoolean(7)
                });
            }

            return destinos;
        }

        public string ScriptCrearLlaveForanea(DefinicionLlaveForaneaModelo llave)
        {
            var script = new StringBuilder();
            script.Append($"ALTER TABLE {NombreCompleto(llave.TablaOrigen)}");
            script.AppendLine(llave.ComprobarExistentes ? " WITH CHECK" : " WITH NOCHECK");
            script.Append($"    ADD {RestriccionLlave(llave.TablaOrigen.Nombre, llave.ColumnaOrigen, llave.Referencia)}");
            script.Append(';');
            return script.ToString();
        }

        private static string RestriccionLlave(string tablaOrigen, string columnaOrigen, ReferenciaModelo referencia)
        {
            var texto = new StringBuilder();
            texto.Append($"CONSTRAINT {Delimitar($"FK_{tablaOrigen}_{referencia.TablaDestino.Nombre}_{columnaOrigen}")}");
            texto.Append($" FOREIGN KEY ({Delimitar(columnaOrigen)})");
            texto.Append($" REFERENCES {NombreCompleto(referencia.TablaDestino)} ({Delimitar(referencia.ColumnaDestino)})");

            if (referencia.AlEliminar != ReglaReferencial.SinAccion)
            {
                texto.Append($" ON DELETE {referencia.AlEliminar.Sql()}");
            }

            if (referencia.AlActualizar != ReglaReferencial.SinAccion)
            {
                texto.Append($" ON UPDATE {referencia.AlActualizar.Sql()}");
            }

            return texto.ToString();
        }
    }
}
