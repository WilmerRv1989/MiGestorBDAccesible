using System.Text;
using System.Text.RegularExpressions;
using MiGestorBDAccesible.Core.Definicion;
using MiGestorBDAccesible.Core.Estructura;

namespace MiGestorBDAccesible.Core.Descripcion
{
    public static class AnalizadorModeloService
    {
        public static List<DetalleTablaModelo> Dependencias(ModeloBaseDatosModelo modelo, DetalleTablaModelo tabla) =>
            tabla.LlavesForaneas
                .Where(l => l.TablaDestino.NombreCompleto != tabla.Tabla.NombreCompleto)
                .Select(l => modelo.Buscar(l.TablaDestino))
                .OfType<DetalleTablaModelo>()
                .Distinct()
                .ToList();

        public static bool SeApuntaASiMisma(DetalleTablaModelo tabla) =>
            tabla.LlavesForaneas.Any(l => l.TablaDestino.NombreCompleto == tabla.Tabla.NombreCompleto);

        public static OrdenCargaModelo OrdenCarga(ModeloBaseDatosModelo modelo)
        {
            var componentes = Componentes(modelo);
            var componenteDe = new Dictionary<DetalleTablaModelo, int>();

            for (var i = 0; i < componentes.Count; i++)
            {
                foreach (var tabla in componentes[i])
                {
                    componenteDe[tabla] = i;
                }
            }

            var niveles = new int[componentes.Count];

            for (var i = 0; i < componentes.Count; i++)
            {
                var dependencias = componentes[i]
                    .SelectMany(t => Dependencias(modelo, t))
                    .Select(d => componenteDe[d])
                    .Where(c => c != i)
                    .ToList();

                niveles[i] = dependencias.Count == 0 ? 0 : dependencias.Max(c => niveles[c]) + 1;
            }

            return new OrdenCargaModelo
            {
                Niveles = Enumerable.Range(0, componentes.Count)
                    .GroupBy(i => niveles[i])
                    .OrderBy(g => g.Key)
                    .Select(g => g.SelectMany(i => componentes[i]).OrderBy(t => modelo.Tablas.IndexOf(t)).ToList())
                    .ToList(),
                Ciclos = componentes.Where(c => c.Count > 1).Select(c => c.OrderBy(t => modelo.Tablas.IndexOf(t)).ToList()).ToList()
            };
        }

        private static List<List<DetalleTablaModelo>> Componentes(ModeloBaseDatosModelo modelo)
        {
            var indice = new Dictionary<DetalleTablaModelo, int>();
            var bajo = new Dictionary<DetalleTablaModelo, int>();
            var pila = new Stack<DetalleTablaModelo>();
            var enPila = new HashSet<DetalleTablaModelo>();
            var componentes = new List<List<DetalleTablaModelo>>();
            var contador = 0;

            void Visitar(DetalleTablaModelo tabla)
            {
                indice[tabla] = bajo[tabla] = contador++;
                pila.Push(tabla);
                enPila.Add(tabla);

                foreach (var dependencia in Dependencias(modelo, tabla))
                {
                    if (!indice.ContainsKey(dependencia))
                    {
                        Visitar(dependencia);
                        bajo[tabla] = Math.Min(bajo[tabla], bajo[dependencia]);
                    }
                    else if (enPila.Contains(dependencia))
                    {
                        bajo[tabla] = Math.Min(bajo[tabla], indice[dependencia]);
                    }
                }

                if (bajo[tabla] == indice[tabla])
                {
                    var componente = new List<DetalleTablaModelo>();
                    DetalleTablaModelo actual;

                    do
                    {
                        actual = pila.Pop();
                        enPila.Remove(actual);
                        componente.Add(actual);
                    }
                    while (actual != tabla);

                    componentes.Add(componente);
                }
            }

            foreach (var tabla in modelo.Tablas.Where(t => !indice.ContainsKey(t)))
            {
                Visitar(tabla);
            }

            return componentes;
        }

        public static List<List<PasoJoinModelo>> CaminosJoin(ModeloBaseDatosModelo modelo, TablaModelo desde, TablaModelo hasta, int maximo = 3)
        {
            var aristas = new Dictionary<string, List<PasoJoinModelo>>();

            void Agregar(TablaModelo anterior, TablaModelo nueva, LlaveForaneaModelo llave)
            {
                if (!aristas.TryGetValue(anterior.NombreCompleto, out var lista))
                {
                    aristas[anterior.NombreCompleto] = lista = [];
                }

                lista.Add(new PasoJoinModelo { Anterior = anterior, Nueva = nueva, Llave = llave });
            }

            foreach (var tabla in modelo.Tablas)
            {
                foreach (var llave in tabla.LlavesForaneas)
                {
                    var destino = modelo.Buscar(llave.TablaDestino);

                    if (destino is null || destino == tabla)
                    {
                        continue;
                    }

                    Agregar(tabla.Tabla, destino.Tabla, llave);
                    Agregar(destino.Tabla, tabla.Tabla, llave);
                }
            }

            if (desde.NombreCompleto == hasta.NombreCompleto)
            {
                return [];
            }

            var distancia = new Dictionary<string, int> { [desde.NombreCompleto] = 0 };
            var llegadas = new Dictionary<string, List<PasoJoinModelo>>();
            var cola = new Queue<string>();
            cola.Enqueue(desde.NombreCompleto);

            while (cola.Count > 0)
            {
                var actual = cola.Dequeue();

                if (actual == hasta.NombreCompleto)
                {
                    break;
                }

                foreach (var paso in aristas.GetValueOrDefault(actual) ?? [])
                {
                    var siguiente = paso.Nueva.NombreCompleto;

                    if (!distancia.TryGetValue(siguiente, out var conocida))
                    {
                        distancia[siguiente] = distancia[actual] + 1;
                        llegadas[siguiente] = [paso];
                        cola.Enqueue(siguiente);
                    }
                    else if (conocida == distancia[actual] + 1)
                    {
                        llegadas[siguiente].Add(paso);
                    }
                }
            }

            var caminos = new List<List<PasoJoinModelo>>();

            void Reconstruir(string tabla, List<PasoJoinModelo> resto)
            {
                if (caminos.Count >= maximo)
                {
                    return;
                }

                if (tabla == desde.NombreCompleto)
                {
                    caminos.Add(resto);
                    return;
                }

                foreach (var paso in llegadas[tabla])
                {
                    Reconstruir(paso.Anterior.NombreCompleto, [paso, .. resto]);
                }
            }

            if (llegadas.ContainsKey(hasta.NombreCompleto))
            {
                Reconstruir(hasta.NombreCompleto, []);
            }

            return caminos;
        }

        public static string ConsultaJoin(DialectoSqlModelo dialecto, TablaModelo desde, List<PasoJoinModelo> camino)
        {
            var alias = new Dictionary<string, string>();

            string Alias(TablaModelo tabla)
            {
                if (alias.TryGetValue(tabla.NombreCompleto, out var existente))
                {
                    return existente;
                }

                var partes = Regex.Split(tabla.Nombre, @"(?<=[a-z])(?=[A-Z])|[_\s]+").Where(p => p.Length > 0).Select(p => p.ToLowerInvariant()).ToArray();
                var base_ = partes.Length > 1 ? string.Concat(partes.Select(p => p[0])) : partes.FirstOrDefault()?[..1] ?? "t";
                var candidato = base_;

                for (var n = 2; alias.ContainsValue(candidato); n++)
                {
                    candidato = partes.Length == 1 && partes[0].Length >= n ? partes[0][..n] : $"{base_}{n}";
                }

                alias[tabla.NombreCompleto] = candidato;
                return candidato;
            }

            var sql = new StringBuilder();
            sql.Append("SELECT *\n");
            sql.Append($"FROM {dialecto.Tabla(desde)} {Alias(desde)}");

            foreach (var paso in camino)
            {
                var nueva = Alias(paso.Nueva);
                var anterior = Alias(paso.Anterior);
                var condiciones = paso.ColumnasNueva.Zip(paso.ColumnasAnterior,
                    (n, a) => $"{nueva}.{dialecto.Columna(n)} = {anterior}.{dialecto.Columna(a)}");

                sql.Append($"\nJOIN {dialecto.Tabla(paso.Nueva)} {nueva} ON {string.Join(" AND ", condiciones)}");
            }

            sql.Append(';');
            return sql.ToString();
        }

        public static bool TieneIndice(DetalleTablaModelo tabla, LlaveForaneaModelo llave)
        {
            var columnas = llave.ColumnasOrigen.Select(c => c.ToLowerInvariant()).ToHashSet();

            return tabla.Indices.Any(i =>
            {
                var clave = i.ColumnasClave.Take(columnas.Count).Select(c => c.ToLowerInvariant()).ToList();
                return clave.Count == columnas.Count && columnas.SetEquals(clave);
            });
        }

        public static List<DetalleTablaModelo> Aisladas(ModeloBaseDatosModelo modelo) =>
            modelo.Tablas.Count < 2
                ? []
                : modelo.Tablas.Where(t =>
                    t.LlavesForaneas.All(l => l.TablaDestino.NombreCompleto == t.Tabla.NombreCompleto) &&
                    t.Referencias.All(r => r.TablaOrigen.NombreCompleto == t.Tabla.NombreCompleto)).ToList();

        public static List<string> Problemas(ModeloBaseDatosModelo modelo)
        {
            var problemas = new List<string>();

            foreach (var tabla in modelo.Tablas.Where(t => t.ClavePrincipal is null))
            {
                problemas.Add($"{tabla.Tabla.NombreCorto()} no tiene clave principal: no hay forma segura de distinguir una fila de otra, y no se puede editar en el panel de filas.");
            }

            foreach (var tabla in modelo.Tablas)
            {
                foreach (var llave in tabla.LlavesForaneas)
                {
                    var origen = tabla.Tabla.NombreCorto();
                    var destino = llave.TablaDestino.NombreCorto();
                    var columnas = string.Join(", ", llave.ColumnasOrigen);

                    if (!llave.Habilitada)
                    {
                        problemas.Add($"La llave foránea {llave.Nombre} de {origen} está deshabilitada: el motor no comprueba que {columnas} exista en {destino}.");
                    }

                    if (!TieneIndice(tabla, llave))
                    {
                        problemas.Add($"La llave foránea {llave.Nombre} de {origen} ({columnas}) no tiene índice: los JOIN con {destino} y los borrados en {destino} recorren toda la tabla {origen}.");
                    }

                    if (modelo.Buscar(llave.TablaDestino) is { } detalleDestino)
                    {
                        foreach (var (columnaOrigen, columnaDestino) in llave.ColumnasOrigen.Zip(llave.ColumnasDestino))
                        {
                            var tipoOrigen = TipoDe(tabla, columnaOrigen);
                            var tipoDestino = TipoDe(detalleDestino, columnaDestino);

                            if (tipoOrigen is not null && tipoDestino is not null && Normalizar(tipoOrigen) != Normalizar(tipoDestino))
                            {
                                problemas.Add($"En la llave foránea {llave.Nombre}, {origen}.{columnaOrigen} es {tipoOrigen} y {destino}.{columnaDestino} es {tipoDestino}: conviene que tengan el mismo tipo.");
                            }
                        }
                    }
                }
            }

            foreach (var tabla in Aisladas(modelo))
            {
                problemas.Add($"{tabla.Tabla.NombreCorto()} no se relaciona con ninguna otra tabla. Si debería, le falta una llave foránea.");
            }

            foreach (var objeto in modelo.Objetos.Where(o => !o.Objeto.Valido))
            {
                var articulo = objeto.Objeto.Tipo.ConArticulo();
                problemas.Add($"{char.ToUpper(articulo[0])}{articulo[1..]} {objeto.Objeto.NombreCompleto} tiene errores de compilación y no se puede usar hasta corregirlos.");
            }

            return problemas;
        }

        private static string? TipoDe(DetalleTablaModelo tabla, string columna) =>
            tabla.Columnas.FirstOrDefault(c => string.Equals(c.Nombre, columna, StringComparison.OrdinalIgnoreCase))?.TipoDatos;

        private static string Normalizar(string tipo) => tipo.Replace(" ", "").ToLowerInvariant();
    }
}
