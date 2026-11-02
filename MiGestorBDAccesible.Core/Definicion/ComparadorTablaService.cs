using MiGestorBDAccesible.Core.Descripcion;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;

namespace MiGestorBDAccesible.Core.Definicion
{
    public static class ComparadorTablaService
    {
        public static DefinicionTablaModelo DesdeDetalle(DetalleTablaModelo detalle, IReadOnlyList<TipoDatosModelo> tipos)
        {
            var clave = detalle.ClavePrincipal?.ColumnasClave.ToList() ?? [];
            var unicas = ColumnasUnicas(detalle);
            var llaves = LlavesSimples(detalle);
            var definicion = new DefinicionTablaModelo { Esquema = detalle.Tabla.Esquema, Nombre = detalle.Tabla.Nombre };

            foreach (var columna in detalle.Columnas)
            {
                var nueva = new DefinicionColumnaModelo
                {
                    Nombre = columna.Nombre,
                    NombreOriginal = columna.Nombre,
                    PermiteNulos = columna.PermiteNulos,
                    EsClavePrincipal = clave.Contains(columna.Nombre),
                    EsUnica = unicas.ContainsKey(columna.Nombre),
                    EsIdentidad = columna.EsIdentidad,
                    Semilla = columna.SemillaIdentidad ?? 1,
                    Incremento = columna.IncrementoIdentidad ?? 1,
                    ValorPredeterminado = Predeterminado(columna.ValorPredeterminado),
                    Formula = columna.Formula,
                    EsPersistente = columna.EsPersistente
                };

                if (!nueva.AplicarTipo(columna.TipoDatos, tipos))
                {
                    nueva.Tipo = columna.TipoDatos;
                }

                nueva.EsIdentidad = columna.EsIdentidad;

                if (llaves.TryGetValue(columna.Nombre, out var llave))
                {
                    nueva.Referencia = new ReferenciaModelo
                    {
                        TablaDestino = llave.TablaDestino,
                        ColumnaDestino = llave.ColumnasDestino[0],
                        AlEliminar = llave.AlEliminar,
                        AlActualizar = llave.AlActualizar
                    };
                }

                definicion.Columnas.Add(nueva);
            }

            return definicion;
        }

        public static PlanModificacionModelo Comparar(
            DetalleTablaModelo original,
            DefinicionTablaModelo nueva,
            IReadOnlyList<TipoDatosModelo> tipos,
            TerminosMotorModelo terminos,
            Func<string, string> nombreGuardado)
        {
            var anterior = DesdeDetalle(original, tipos);
            var porNombre = anterior.Columnas.ToDictionary(c => c.Nombre, StringComparer.Ordinal);
            var unicas = ColumnasUnicas(original);
            var llaves = LlavesSimples(original);
            var conservadas = nueva.Columnas.Where(c => c.NombreOriginal is not null && porNombre.ContainsKey(c.NombreOriginal)).ToList();
            var agregadas = nueva.Columnas.Where(c => c.NombreOriginal is null).ToList();
            var cambios = new List<CambioTablaModelo>();
            var motivos = new List<string>();
            var filas = original.Filas == 1 ? "su única fila" : $"sus {original.Filas:N0} filas";

            string Final(DefinicionColumnaModelo columna) => NombreFinal(columna.Nombre, columna.NombreOriginal, nombreGuardado);
            DefinicionColumnaModelo? Conservada(string nombreOriginal) => conservadas.FirstOrDefault(c => c.NombreOriginal == nombreOriginal);

            var tablaFinal = NombreFinal(nueva.Nombre, original.Tabla.Nombre, nombreGuardado);

            if (tablaFinal != original.Tabla.Nombre)
            {
                cambios.Add(new CambioTablaModelo
                {
                    Tipo = TipoCambioTabla.RenombrarTabla,
                    Frase = $"Cambiar el nombre de la tabla {original.Tabla.Nombre} a {tablaFinal} (RENAME)"
                });
            }

            foreach (var columna in anterior.Columnas.Where(c => Conservada(c.Nombre) is null))
            {
                cambios.Add(new CambioTablaModelo
                {
                    Tipo = TipoCambioTabla.QuitarColumna,
                    Anterior = columna,
                    Frase = $"Quitar la columna {columna.Nombre} y sus datos (DROP COLUMN)"
                });
            }

            var ordenNuevo = conservadas.Select(c => c.NombreOriginal!).ToList();
            var ordenAnterior = anterior.Columnas.Select(c => c.Nombre).Where(ordenNuevo.Contains).ToList();

            if (!ordenNuevo.SequenceEqual(ordenAnterior))
            {
                motivos.Add($"Cambiar el orden de las columnas obliga a crear la tabla de nuevo y copiar {filas}.");
            }

            var primeraAgregada = nueva.Columnas.FindIndex(c => c.NombreOriginal is null);

            if (primeraAgregada >= 0 && nueva.Columnas.Skip(primeraAgregada).Any(c => c.NombreOriginal is not null))
            {
                motivos.Add($"Agregar una columna entre las que ya existen obliga a crear la tabla de nuevo y copiar {filas}. Si la dejas al final, se agrega sin rehacer la tabla.");
            }

            foreach (var columna in conservadas)
            {
                var antes = porNombre[columna.NombreOriginal!];
                var nombre = Final(columna);

                if (nombre != antes.Nombre)
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.RenombrarColumna,
                        Anterior = antes,
                        Nueva = columna,
                        Frase = $"Cambiar el nombre de la columna {antes.Nombre} a {nombre} (RENAME COLUMN)"
                    });
                }

                if (antes.EsCalculada)
                {
                    continue;
                }

                if (Normalizar(columna.TipoCompleto) != Normalizar(antes.TipoCompleto))
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.CambiarTipo,
                        Anterior = antes,
                        Nueva = columna,
                        Frase = $"Cambiar el tipo de {nombre} de {antes.TipoCompleto} a {columna.TipoCompleto} ({terminos.ModificarColumna})"
                    });
                }

                if (columna.PermiteNulos != antes.PermiteNulos)
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.CambiarNulos,
                        Anterior = antes,
                        Nueva = columna,
                        Frase = columna.PermiteNulos
                            ? $"Hacer opcional la columna {nombre} (NULL)"
                            : $"Hacer obligatoria la columna {nombre} (NOT NULL)"
                    });
                }

                var predeterminadoAntes = Predeterminado(antes.ValorPredeterminado);
                var predeterminadoNuevo = Predeterminado(columna.ValorPredeterminado);

                if (predeterminadoAntes != predeterminadoNuevo)
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.CambiarPredeterminado,
                        Anterior = antes,
                        Nueva = columna,
                        Frase = (predeterminadoAntes, predeterminadoNuevo) switch
                        {
                            (null, _) => $"Poner el valor predeterminado {predeterminadoNuevo} a {nombre} (DEFAULT)",
                            (_, null) => $"Quitar el valor predeterminado de {nombre} (DEFAULT)",
                            _ => $"Cambiar el valor predeterminado de {nombre} de {predeterminadoAntes} a {predeterminadoNuevo} (DEFAULT)"
                        }
                    });
                }

                if (columna.EsIdentidad != antes.EsIdentidad
                    || columna.EsIdentidad && (columna.Semilla != antes.Semilla || columna.Incremento != antes.Incremento))
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.CambiarIdentidad,
                        Anterior = antes,
                        Nueva = columna,
                        Frase = (antes.EsIdentidad, columna.EsIdentidad) switch
                        {
                            (false, _) => $"Convertir {nombre} en número automático ({terminos.NombreIdentidad})",
                            (_, false) => $"Quitar el número automático de {nombre} ({terminos.NombreIdentidad})",
                            _ => $"Hacer que el número automático de {nombre} empiece en {columna.Semilla} y suba de {columna.Incremento} ({terminos.NombreIdentidad})"
                        }
                    });
                }
            }

            var claveAntes = anterior.Columnas.Where(c => c.EsClavePrincipal).Select(c => c.Nombre).ToList();
            var claveNueva = nueva.Columnas.Where(c => c.EsClavePrincipal).ToList();
            var mismaClave = claveNueva.All(c => c.NombreOriginal is not null)
                && claveNueva.Select(c => c.NombreOriginal!).OrderBy(n => n, StringComparer.Ordinal)
                    .SequenceEqual(claveAntes.OrderBy(n => n, StringComparer.Ordinal));

            if (!mismaClave)
            {
                if (original.ClavePrincipal is { } clave)
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.QuitarClavePrincipal,
                        Restriccion = clave.Nombre,
                        Columnas = claveAntes,
                        Frase = $"Quitar la clave principal {clave.Nombre}, de {NarradorTablaService.Unir(claveAntes)} (PRIMARY KEY)"
                    });
                }

                if (claveNueva.Count > 0)
                {
                    var columnas = claveNueva.Select(Final).ToList();
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.CrearClavePrincipal,
                        Columnas = columnas,
                        Frase = $"Crear la clave principal con {NarradorTablaService.Unir(columnas)} (PRIMARY KEY)"
                    });
                }
            }

            foreach (var (columnaUnica, restriccion) in unicas)
            {
                if (Conservada(columnaUnica) is not { EsUnica: true })
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.QuitarUnica,
                        Anterior = porNombre.GetValueOrDefault(columnaUnica),
                        Restriccion = restriccion,
                        Frase = $"Quitar la restricción de valor único de {columnaUnica} (UNIQUE)"
                    });
                }
            }

            var claveSimple = claveNueva.Count == 1 ? claveNueva[0] : null;

            foreach (var columna in nueva.Columnas.Where(c => c.EsUnica && c != claveSimple))
            {
                if (columna.NombreOriginal is not null && unicas.ContainsKey(columna.NombreOriginal))
                {
                    continue;
                }

                cambios.Add(new CambioTablaModelo
                {
                    Tipo = TipoCambioTabla.AgregarUnica,
                    Anterior = columna.NombreOriginal is null ? null : porNombre.GetValueOrDefault(columna.NombreOriginal),
                    Nueva = columna,
                    Frase = $"Hacer que {Final(columna)} no se pueda repetir (UNIQUE)"
                });
            }

            foreach (var (columnaLlave, llave) in llaves)
            {
                var conservada = Conservada(columnaLlave);

                if (conservada?.Referencia is null || !MismaReferencia(porNombre[columnaLlave].Referencia!, conservada.Referencia))
                {
                    cambios.Add(new CambioTablaModelo
                    {
                        Tipo = TipoCambioTabla.QuitarLlaveForanea,
                        Anterior = porNombre[columnaLlave],
                        Restriccion = llave.Nombre,
                        Frase = $"Quitar la llave foránea {llave.Nombre}, de {columnaLlave} hacia {llave.TablaDestino.NombreCorto()}.{llave.ColumnasDestino[0]} (FOREIGN KEY)"
                    });
                }
            }

            foreach (var columna in nueva.Columnas.Where(c => c.Referencia is { ColumnaDestino.Length: > 0 }))
            {
                var antes = columna.NombreOriginal is null ? null : porNombre.GetValueOrDefault(columna.NombreOriginal);

                if (antes?.Referencia is not null && MismaReferencia(antes.Referencia, columna.Referencia!))
                {
                    continue;
                }

                cambios.Add(new CambioTablaModelo
                {
                    Tipo = TipoCambioTabla.AgregarLlaveForanea,
                    Anterior = antes,
                    Nueva = columna,
                    Frase = $"Hacer que {Final(columna)} apunte a {columna.Referencia!.TablaDestino.NombreCorto()}.{columna.Referencia.ColumnaDestino} (FOREIGN KEY)"
                });
            }

            foreach (var columna in agregadas)
            {
                var detalle = new List<string> { columna.TipoCompleto };

                if (columna.EsIdentidad)
                {
                    detalle.Add($"número automático ({terminos.NombreIdentidad})");
                }

                if (Predeterminado(columna.ValorPredeterminado) is { } valor)
                {
                    detalle.Add($"predeterminado {valor}");
                }

                detalle.Add(columna.PermiteNulos ? "opcional" : "obligatoria");

                cambios.Add(new CambioTablaModelo
                {
                    Tipo = TipoCambioTabla.AgregarColumna,
                    Nueva = columna,
                    Frase = $"Agregar la columna {Final(columna)}: {string.Join(", ", detalle)} (ADD)"
                });
            }

            return new PlanModificacionModelo
            {
                Original = original,
                Nueva = nueva,
                Cambios = cambios.OrderBy(c => c.Tipo).ToList(),
                MotivosRecreacion = motivos
            };
        }

        public static string? Predeterminado(string? valor) =>
            string.IsNullOrWhiteSpace(valor) ? null : NarradorTablaService.QuitarParentesisExternos(valor.Trim()).Trim();

        public static Dictionary<string, string> ColumnasUnicas(DetalleTablaModelo detalle) =>
            detalle.Indices
                .Where(i => i.EsRestriccionUnica && i.ColumnasClave.Count() == 1)
                .GroupBy(i => i.ColumnasClave.First(), StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Nombre, StringComparer.Ordinal);

        public static Dictionary<string, LlaveForaneaModelo> LlavesSimples(DetalleTablaModelo detalle) =>
            detalle.LlavesForaneas
                .Where(l => l.ColumnasOrigen.Count == 1)
                .GroupBy(l => l.ColumnasOrigen[0], StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        public static string NombreFinal(string? escrito, string? original, Func<string, string> nombreGuardado)
        {
            var limpio = escrito?.Trim() ?? "";

            if (limpio.Length == 0)
            {
                return original ?? "";
            }

            return original is not null && limpio == original ? original : nombreGuardado(limpio);
        }

        public static Dictionary<string, string> Renombres(PlanModificacionModelo plan, Func<string, string> nombreGuardado) =>
            plan.Nueva.Columnas.Where(c => c.NombreOriginal is not null)
                .ToDictionary(c => c.NombreOriginal!, c => NombreFinal(c.Nombre, c.NombreOriginal, nombreGuardado), StringComparer.Ordinal);

        public static List<string>? Mapear(IEnumerable<string> columnas, Dictionary<string, string> renombres)
        {
            var lista = new List<string>();

            foreach (var columna in columnas)
            {
                if (!renombres.TryGetValue(columna, out var nueva))
                {
                    return null;
                }

                lista.Add(nueva);
            }

            return lista;
        }

        private static bool MismaReferencia(ReferenciaModelo a, ReferenciaModelo b) =>
            string.Equals(a.TablaDestino.NombreCompleto, b.TablaDestino.NombreCompleto, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.ColumnaDestino, b.ColumnaDestino, StringComparison.OrdinalIgnoreCase)
            && a.AlEliminar == b.AlEliminar
            && a.AlActualizar == b.AlActualizar;

        private static string Normalizar(string tipo) => tipo.Replace(" ", "").ToLowerInvariant();
    }
}
