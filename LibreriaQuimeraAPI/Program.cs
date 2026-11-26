using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurar CORS para permitir peticiones desde tu frontend (HTML/JS)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("AllowAll");

string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=LibreriaQuimera;Integrated Security=True;TrustServerCertificate=True;";

// ==========================================
// ENDPOINTS DE LA API (Minimal API)
// ==========================================

app.MapGet("/obtener-productos", () =>
{
    var libros = new List<object>();
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            SELECT l.id_libro, l.titulo, l.precio, l.stock, l.autor, l.imagen, c.nombre AS categoria 
            FROM Libros l 
            INNER JOIN Categorias c ON l.id_categoria = c.id_categoria;";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    libros.Add(new
                    {
                        id = reader["id_libro"].ToString(),
                        titulo = reader["titulo"].ToString(),
                        precio = Convert.ToDecimal(reader["precio"]),
                        stock = Convert.ToInt32(reader["stock"]),
                        autor = reader["autor"].ToString(),
                        imagen = reader["imagen"].ToString(),
                        categoria = reader["categoria"].ToString()
                    });
                }
            }
        }
    }
    return Results.Ok(libros);
});

// --- RUTA: Registro de Usuarios ---
app.MapPost("/api/registro", (UsuarioRegistro request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "INSERT INTO Usuarios (nombre, apellido, correo, Contrasena, telefono) VALUES (@Nombre, @Apellido, @Correo, @Contrasena, @Telefono)";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Nombre", request.Nombre);
            cmd.Parameters.AddWithValue("@Apellido", request.Apellido);
            cmd.Parameters.AddWithValue("@Correo", request.Correo);
            cmd.Parameters.AddWithValue("@Contrasena", request.Contrasena);
            cmd.Parameters.AddWithValue("@Telefono", request.Telefono ?? (object)DBNull.Value);

            try
            {
                cmd.ExecuteNonQuery();
                return Results.Ok(new { mensaje = "Usuario registrado exitosamente" });
            }
            catch (SqlException)
            {
                return Results.BadRequest(new { mensaje = "El correo ya está registrado u ocurrió un error." });
            }
        }
    }
});

// --- RUTA: Login de Usuarios ---
app.MapPost("/api/login", (UsuarioLogin request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "SELECT id_usuario, nombre FROM Usuarios WHERE correo = @Correo AND Contrasena = @Contrasena";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Correo", request.Correo);
            cmd.Parameters.AddWithValue("@Contrasena", request.Contrasena);

            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    return Results.Ok(new
                    {
                        id = Convert.ToInt32(reader["id_usuario"]),
                        nombre = reader["nombre"].ToString()
                    });
                }
                else
                {
                    return Results.Unauthorized();
                }
            }
        }
    }
});

// --- RUTA: Procesar Compra y Actualizar Inventario ---
app.MapPost("/api/checkout", (CheckoutRequest request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        SqlTransaction transaction = conn.BeginTransaction();
        try
        {
            // 1. Insertar en Pedidos
            string queryPedido = "INSERT INTO Pedidos (id_usuario, total, direccion, ciudad, codigo_postal, telefono_envio) OUTPUT INSERTED.id_pedido VALUES (@IdUsuario, @Total, @Direccion, @Ciudad, @CP, @TelEnvio)";
            int idPedido = 0;
            using (SqlCommand cmdPedido = new SqlCommand(queryPedido, conn, transaction))
            {
                cmdPedido.Parameters.AddWithValue("@IdUsuario", request.IdUsuario);
                cmdPedido.Parameters.AddWithValue("@Total", request.Total);
                cmdPedido.Parameters.AddWithValue("@Direccion", request.Direccion);
                cmdPedido.Parameters.AddWithValue("@Ciudad", request.Ciudad);
                cmdPedido.Parameters.AddWithValue("@CP", request.CodigoPostal);
                cmdPedido.Parameters.AddWithValue("@TelEnvio", request.TelefonoEnvio);

                idPedido = (int)cmdPedido.ExecuteScalar();
            }

            // 2. Insertar Detalles del Pedido y actualizar el stock
            foreach (var item in request.Carrito)
            {
                string queryDetalle = "INSERT INTO Detalles_Pedido (id_pedido, id_libro, cantidad, precio_unitario) VALUES (@IdPedido, @IdLibro, @Cantidad, @Precio)";
                using (SqlCommand cmdDetalle = new SqlCommand(queryDetalle, conn, transaction))
                {
                    cmdDetalle.Parameters.AddWithValue("@IdPedido", idPedido);
                    cmdDetalle.Parameters.AddWithValue("@IdLibro", item.IdLibro);
                    cmdDetalle.Parameters.AddWithValue("@Cantidad", item.Cantidad);
                    cmdDetalle.Parameters.AddWithValue("@Precio", item.PrecioUnitario);
                    cmdDetalle.ExecuteNonQuery();
                }

                string queryStock = "UPDATE Libros SET stock = stock - @Cantidad WHERE id_libro = @IdLibro";
                using (SqlCommand cmdStock = new SqlCommand(queryStock, conn, transaction))
                {
                    cmdStock.Parameters.AddWithValue("@Cantidad", item.Cantidad);
                    cmdStock.Parameters.AddWithValue("@IdLibro", item.IdLibro);
                    cmdStock.ExecuteNonQuery();
                }
            }

            transaction.Commit();
            return Results.Ok(new { mensaje = "Compra procesada con éxito", idPedido = idPedido });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return Results.BadRequest(new { mensaje = "Error al procesar la compra", detalle = ex.Message });
        }
    }
});

// --- RUTA: Enviar Mensaje de Contacto ---
app.MapPost("/enviar-contacto", (ContactoRequest request) =>
{
    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string query = "INSERT INTO Mensajes_Contacto (nombre, telefono, correo, mensaje, fecha_envio) VALUES (@Nombre, @Telefono, @Correo, @Mensaje, GETDATE())";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Nombre", request.Nombre ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Telefono", request.Telefono ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Correo", request.Correo ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Mensaje", request.Mensaje ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
        return Results.Ok(new { exito = true, mensaje = "¡Mensaje enviado con éxito! Gracias por escribirnos." });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { exito = false, mensaje = "Error en el servidor: " + ex.Message });
    }
});

app.MapPost("/api/cambiar-password", async (CambioPasswordRequest request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "UPDATE Usuarios SET Contrasena = @Contrasena WHERE correo = @Correo";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Contrasena", request.NuevaPassword);
            cmd.Parameters.AddWithValue("@Correo", request.Correo);

            int filasAfectadas = cmd.ExecuteNonQuery();

            if (filasAfectadas > 0)
            {
                return Results.Ok(new { mensaje = "¡Contraseña actualizada con éxito!" });
            }
            else
            {
                return Results.BadRequest(new { mensaje = "No se encontró el usuario." });
            }
        }
    }
});

string normalizarTexto(string texto)
{
    if (string.IsNullOrWhiteSpace(texto)) return "";

    string limpio = texto.Trim().ToLower();
    return limpio
        .Replace("á", "a")
        .Replace("é", "e")
        .Replace("í", "i")
        .Replace("ó", "o")
        .Replace("ú", "u");
}

string estimarLlegada(string ciudad)
{
    string t = normalizarTexto(ciudad);

    // 1. Local / Estado base (1 a 2 días hábiles)
    string[] zonaLocal = { "campeche", "ciudad del carmen" };
    if (zonaLocal.Any(x => t.Contains(x)))
        return "1 a 2 días hábiles";

    // 2. Sureste y península (2 a 4 días hábiles)
    string[] zonaSureste = {
        "yucatan", "merida", "valladolid", "tizimin",
        "quintana roo", "cancun", "playa del carmen", "chetumal", "tulum",
        "tabasco", "villahermosa", "cardenas",
        "chiapas", "tuxtla", "san cristobal", "tapachula",
        "oaxaca", "huatepec", "salina cruz",
        "veracruz", "xalapa", "coatza", "coatzacoalcos", "poza rica"
    };
    if (zonaSureste.Any(x => t.Contains(x)))
        return "2 a 4 días hábiles";

    // 3. Centro, Bajío y Occidente (3 a 5 días hábiles)
    string[] zonaCentro = {
        "ciudad de mexico", "cdmx", "df", "distrito federal",
        "estado de mexico", "edomex", "toluca", "ecatepec", "nezahualcoyotl", "naucalpan",
        "puebla", "tehuacan",
        "morelos", "cuernavaca",
        "tlaxcala",
        "hidalgo", "pachuca",
        "queretaro",
        "guanajuato", "leon", "irapuato", "celaya",
        "michoacan", "morelia", "uruapan",
        "jalisco", "guadalajara", "zapopan", "puerto vallarta",
        "colima", "manzanillo",
        "nayarit", "tepic",
        "guerrero", "acapulco", "chilpancingo",
        "san luis potosi",
        "aguascalientes",
        "zacatecas"
    };
    if (zonaCentro.Any(x => t.Contains(x)))
        return "3 a 5 días hábiles";

    // 4. Norte y Noroeste (4 a 6 días hábiles)
    string[] zonaNorte = {
        "nuevo leon", "monterrey", "san nicolas", "guadalupe", "apodaca",
        "coahuila", "saltillo", "torreon",
        "tamaulipas", "tampico", "reynosa", "matamoros", "ciudad victoria",
        "chihuahua", "juarez",
        "durango", "gomez palacio",
        "sinaloa", "culiacan", "mazatlan",
        "sonora", "hermosillo", "obregon", "nogales",
        "baja california sur", "la paz", "los cabos", "cabo san lucas",
        "baja california", "tijuana", "mexicali", "ensenada"
    };
    if (zonaNorte.Any(x => t.Contains(x)))
        return "4 a 6 días hábiles";

    return "4 a 6 días hábiles";
}

// --- RUTA: Consultar Compras del Usuario ---
app.MapGet("/mis-compras/{idUsuario}", (int idUsuario) =>
{
    var listaCompras = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select 
                p.id_pedido,
                p.total,
                p.direccion,
                p.ciudad,
                isnull(p.estado, 'Pendiente') as estado,
                stuff((
                    select ' | ' + l.titulo + ' (x' + cast(d.cantidad as varchar(10)) + ')'
                    from Detalles_Pedido d
                    inner join Libros l on d.id_libro = l.id_libro
                    where d.id_pedido = p.id_pedido
                    for xml path(''), type
                ).value('.', 'nvarchar(max)'), 1, 3, '') as libros
            from Pedidos p
            where p.id_usuario = @IdUsuario
            order by p.id_pedido desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string estadoActual = reader["estado"]?.ToString() ?? "Pendiente";
                    string direccionPedido = reader["direccion"] == DBNull.Value ? "Sin dirección" : reader["direccion"].ToString()!;
                    string ciudadPedido = reader["ciudad"] == DBNull.Value ? "Sin ciudad" : reader["ciudad"].ToString()!;
                    string librosTexto = reader["libros"] == DBNull.Value ? "Sin detalle" : reader["libros"].ToString()!;

                    listaCompras.Add(new
                    {
                        idPedido = reader["id_pedido"],
                        total = reader["total"],
                        direccion = direccionPedido,
                        ciudad = ciudadPedido,
                        estado = estadoActual,
                        libros = librosTexto,
                        estimadoLlegada = estadoActual == "Cancelado" ? "Pedido cancelado" : estimarLlegada(ciudadPedido)
                    });
                }
            }
        }
    }

    return Results.Ok(listaCompras);
});

app.MapGet("/mis-compras-correo/{correo}", (string correo) =>
{
    var listaCompras = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select 
                p.id_pedido,
                p.total,
                p.ciudad,
                isnull(p.estado, 'Pendiente') as estado,
                stuff((
                    select ', ' + l.titulo + ' (x' + cast(d.cantidad as varchar(10)) + ')'
                    from Detalles_Pedido d
                    inner join Libros l on d.id_libro = l.id_libro
                    where d.id_pedido = p.id_pedido
                    for xml path(''), type
                ).value('.', 'nvarchar(max)'), 1, 2, '') as libros
            from Pedidos p
            inner join Usuarios u on p.id_usuario = u.id_usuario
            where u.correo = @Correo
            order by p.id_pedido desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Correo", correo);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string estadoActual = reader["estado"]?.ToString() ?? "Pendiente";
                    string ciudadPedido = reader["ciudad"]?.ToString() ?? "";
                    string librosTexto = reader["libros"] == DBNull.Value ? "Sin detalle" : reader["libros"].ToString()!;

                    listaCompras.Add(new
                    {
                        idPedido = reader["id_pedido"],
                        total = reader["total"],
                        estado = estadoActual,
                        libros = librosTexto,
                        estimadoLlegada = estadoActual == "Cancelado" ? "Pedido cancelado" : estimarLlegada(ciudadPedido)
                    });
                }
            }
        }
    }

    return Results.Ok(listaCompras);
});

// --- RUTA: Cancelar Compra y Devolver Stock ---
app.MapPost("/cancelar-compra/{idPedido}", (int idPedido) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        SqlTransaction transaccion = conn.BeginTransaction();

        try
        {
            string queryStock = @"
                update Libros
                set stock = stock + d.cantidad
                from Libros l
                inner join Detalles_Pedido d on l.id_libro = d.id_libro
                where d.id_pedido = @IdPedido";

            using (SqlCommand cmdStock = new SqlCommand(queryStock, conn, transaccion))
            {
                cmdStock.Parameters.AddWithValue("@IdPedido", idPedido);
                cmdStock.ExecuteNonQuery();
            }

            string queryEstado = "update Pedidos set estado = 'Cancelado' where id_pedido = @IdPedido";
            using (SqlCommand cmdEstado = new SqlCommand(queryEstado, conn, transaccion))
            {
                cmdEstado.Parameters.AddWithValue("@IdPedido", idPedido);
                cmdEstado.ExecuteNonQuery();
            }

            transaccion.Commit();
            return Results.Ok(new { exito = true, mensaje = "Compra cancelada y stock devuelto." });
        }
        catch (Exception ex)
        {
            transaccion.Rollback();
            return Results.Ok(new { exito = false, mensaje = "Error al cancelar compra: " + ex.Message });
        }
    }
});




// --- RUTA: Eliminar Comentario de Contacto ---
app.MapDelete("/eliminar-comentario/{idMensaje}", (int idMensaje) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "delete from Mensajes_Contacto where id_mensaje = @IdMensaje";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdMensaje", idMensaje);
            int filas = cmd.ExecuteNonQuery();

            if (filas > 0)
            {
                return Results.Ok(new { exito = true, mensaje = "Comentario eliminado." });
            }
            return Results.NotFound(new { exito = false, mensaje = "No se encontró el comentario." });
        }
    }
});





// --- RUTA: Obtener datos del perfil por ID ---
app.MapGet("/api/perfil/{idUsuario}", (int idUsuario) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select id_usuario, nombre, apellido, correo, telefono, direccion, ciudad, codigo_postal
            from Usuarios
            where id_usuario = @IdUsuario";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                    return Results.NotFound(new { mensaje = "Usuario no encontrado." });

                return Results.Ok(new
                {
                    id = reader["id_usuario"],
                    nombre = reader["nombre"]?.ToString() ?? "",
                    apellido = reader["apellido"]?.ToString() ?? "",
                    correo = reader["correo"]?.ToString() ?? "",
                    telefono = reader["telefono"]?.ToString() ?? "",
                    direccion = reader["direccion"]?.ToString() ?? "",
                    ciudad = reader["ciudad"]?.ToString() ?? "",
                    codigo_postal = reader["codigo_postal"]?.ToString() ?? ""
                });
            }
        }
    }
});

// --- RUTA: Actualizar perfil (correo NO editable) ---
app.MapPut("/api/perfil/{idUsuario}", (int idUsuario, PerfilUpdateDto datos) =>
{
    if (string.IsNullOrWhiteSpace(datos.Nombre))
        return Results.BadRequest(new { mensaje = "El nombre es obligatorio." });

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string query = string.IsNullOrWhiteSpace(datos.Contrasena)
            ? @"update Usuarios
                set nombre = @Nombre,
                    apellido = @Apellido,
                    telefono = @Telefono,
                    direccion = @Direccion,
                    ciudad = @Ciudad,
                    codigo_postal = @CodigoPostal
                where id_usuario = @IdUsuario"
            : @"update Usuarios
                set nombre = @Nombre,
                    apellido = @Apellido,
                    contrasena = @Contrasena,
                    telefono = @Telefono,
                    direccion = @Direccion,
                    ciudad = @Ciudad,
                    codigo_postal = @CodigoPostal
                where id_usuario = @IdUsuario";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            cmd.Parameters.AddWithValue("@Nombre", datos.Nombre.Trim());
            cmd.Parameters.AddWithValue("@Apellido", (datos.Apellido ?? "").Trim());
            cmd.Parameters.AddWithValue("@Telefono", (datos.Telefono ?? "").Trim());
            cmd.Parameters.AddWithValue("@Direccion", (datos.Direccion ?? "").Trim());
            cmd.Parameters.AddWithValue("@Ciudad", (datos.Ciudad ?? "").Trim());
            cmd.Parameters.AddWithValue("@CodigoPostal", (datos.CodigoPostal ?? "").Trim());

            if (!string.IsNullOrWhiteSpace(datos.Contrasena))
                cmd.Parameters.AddWithValue("@Contrasena", datos.Contrasena);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return Results.NotFound(new { mensaje = "No se pudo actualizar el perfil." });
        }
    }

    return Results.Ok(new { mensaje = "Perfil actualizado correctamente." });
});







// --- ADMIN: Obtener categorías para formulario de libros ---
app.MapGet("/api/admin/categorias", () =>
{
    var categorias = new List<object>();
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "select id_categoria, nombre from Categorias order by id_categoria";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                categorias.Add(new
                {
                    idCategoria = Convert.ToInt32(reader["id_categoria"]),
                    nombre = reader["nombre"].ToString() ?? ""
                });
            }
        }
    }
    return Results.Ok(categorias);
});

// --- ADMIN: Agregar nuevo libro ---
// --- ADMIN: Agregar nuevo libro ---
app.MapPost("/api/admin/libros", (NuevoLibroAdminDto libro) =>
{
    if (string.IsNullOrWhiteSpace(libro.Titulo) || string.IsNullOrWhiteSpace(libro.Autor))
        return Results.BadRequest(new { mensaje = "Título y autor son obligatorios." });

    int idCategoria = libro.IdCategoria > 0 ? libro.IdCategoria : 1;
    string imagen = string.IsNullOrWhiteSpace(libro.Imagen) ? "./img/default-book.jpg" : libro.Imagen.Trim();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        // Buscamos el número más alto dentro de los id_libro existentes (ej. 'libroRomance-20' -> 20)
        int maxNum = 0;
        string queryIds = "select id_libro from Libros";
        using (SqlCommand cmdIds = new SqlCommand(queryIds, conn))
        using (SqlDataReader readerIds = cmdIds.ExecuteReader())
        {
            while (readerIds.Read())
            {
                string idTexto = readerIds["id_libro"]?.ToString() ?? "";
                string soloDigitos = new string(idTexto.Where(char.IsDigit).ToArray());
                if (int.TryParse(soloDigitos, out int num) && num > maxNum)
                {
                    maxNum = num;
                }
            }
        }

        string nuevoIdLibro = $"libro-{maxNum + 1}";

        string query = @"
            insert into Libros (id_libro, titulo, autor, precio, stock, imagen, id_categoria)
            values (@IdLibro, @Titulo, @Autor, @Precio, @Stock, @Imagen, @IdCategoria)";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdLibro", nuevoIdLibro);
            cmd.Parameters.AddWithValue("@Titulo", libro.Titulo.Trim());
            cmd.Parameters.AddWithValue("@Autor", libro.Autor.Trim());
            cmd.Parameters.AddWithValue("@Precio", libro.Precio);
            cmd.Parameters.AddWithValue("@Stock", libro.Stock);
            cmd.Parameters.AddWithValue("@Imagen", imagen);
            cmd.Parameters.AddWithValue("@IdCategoria", idCategoria);

            cmd.ExecuteNonQuery();
        }
    }

    return Results.Ok(new { mensaje = "Libro agregado correctamente al catálogo." });
});

// --- ADMIN: Listar todos los pedidos ---
app.MapGet("/api/admin/pedidos", () =>
{
    var pedidos = new List<object>();
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select
                p.id_pedido,
                p.total,
                p.direccion,
                p.ciudad,
                isnull(p.estado, 'Pendiente') as estado,
                u.nombre + ' ' + u.apellido as cliente,
                u.correo,
                stuff((
                    select ' | ' + l.titulo + ' (x' + cast(d.cantidad as varchar(10)) + ')'
                    from Detalles_Pedido d
                    inner join Libros l on d.id_libro = l.id_libro
                    where d.id_pedido = p.id_pedido
                    for xml path(''), type
                ).value('.', 'nvarchar(max)'), 1, 3, '') as libros
            from Pedidos p
            left join Usuarios u on p.id_usuario = u.id_usuario
            order by p.id_pedido desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                pedidos.Add(new
                {
                    idPedido = Convert.ToInt32(reader["id_pedido"]),
                    total = Convert.ToDecimal(reader["total"]),
                    direccion = reader["direccion"] == DBNull.Value ? "" : reader["direccion"].ToString(),
                    ciudad = reader["ciudad"] == DBNull.Value ? "" : reader["ciudad"].ToString(),
                    estado = reader["estado"]?.ToString() ?? "Pendiente",
                    cliente = reader["cliente"] == DBNull.Value ? "Cliente" : reader["cliente"].ToString(),
                    correo = reader["correo"] == DBNull.Value ? "" : reader["correo"].ToString(),
                    libros = reader["libros"] == DBNull.Value ? "Sin detalle" : reader["libros"].ToString()
                });
            }
        }
    }
    return Results.Ok(pedidos);
});

// --- ADMIN: Actualizar estado de un pedido ---
app.MapPut("/api/admin/pedidos/{idPedido}/estado", (int idPedido, EstadoPedidoDto req) =>
{
    if (string.IsNullOrWhiteSpace(req.Estado))
        return Results.BadRequest(new { mensaje = "El estado es obligatorio." });

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "update Pedidos set estado = @Estado where id_pedido = @IdPedido";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Estado", req.Estado.Trim());
            cmd.Parameters.AddWithValue("@IdPedido", idPedido);
            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return Results.NotFound(new { mensaje = "Pedido no encontrado." });
        }
    }
    return Results.Ok(new { mensaje = "Estado actualizado correctamente." });
});




// --- ADMIN: Eliminar libro del catálogo ---
app.MapDelete("/api/admin/libros/{idLibro}", (string idLibro) =>
{
    if (string.IsNullOrWhiteSpace(idLibro))
        return Results.BadRequest(new { mensaje = "ID de libro no válido." });

    string idLimpio = Uri.UnescapeDataString(idLibro).Trim();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "delete from Libros where rtrim(ltrim(id_libro)) = @IdLibro";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdLibro", idLimpio);

            try
            {
                int filas = cmd.ExecuteNonQuery();
                if (filas == 0)
                    return Results.NotFound(new { mensaje = $"No se encontró el libro con ID '{idLimpio}'." });

                return Results.Ok(new { mensaje = "Libro eliminado correctamente." });
            }
            catch (SqlException)
            {
                return Results.BadRequest(new { mensaje = "No se puede eliminar porque el libro ya está en un pedido." });
            }
        }
    }
});


app.MapGet("/api/admin/ping", () => Results.Ok(new { ok = true, mensaje = "API actualizada" }));





// --- ADMIN: Crear nueva categoría ---
app.MapPost("/api/admin/categorias", (NuevaCategoriaDto req) =>
{
    if (string.IsNullOrWhiteSpace(req.Nombre))
        return Results.BadRequest(new { mensaje = "El nombre de la categoría es obligatorio." });

    string nombreLimpio = req.Nombre.Trim();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        // Evita duplicados (ignorando mayúsculas/minúsculas)
        string queryExiste = "select count(*) from Categorias where lower(nombre) = lower(@Nombre)";
        using (SqlCommand cmdExiste = new SqlCommand(queryExiste, conn))
        {
            cmdExiste.Parameters.AddWithValue("@Nombre", nombreLimpio);
            int existe = (int)cmdExiste.ExecuteScalar();
            if (existe > 0)
                return Results.BadRequest(new { mensaje = "Esa categoría ya existe." });
        }

        string queryInsert = "insert into Categorias (nombre) values (@Nombre)";
        using (SqlCommand cmdInsert = new SqlCommand(queryInsert, conn))
        {
            cmdInsert.Parameters.AddWithValue("@Nombre", nombreLimpio);
            cmdInsert.ExecuteNonQuery();
        }
    }

    return Results.Ok(new { mensaje = $"Categoría '{nombreLimpio}' creada correctamente." });
});




// --- ADMIN: Eliminar categoría ---
app.MapDelete("/api/admin/categorias/{id:int}", (int id) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string queryLibros = "select count(*) from Libros where id_categoria = @Id";
        using (SqlCommand cmdLibros = new SqlCommand(queryLibros, conn))
        {
            cmdLibros.Parameters.AddWithValue("@Id", id);
            int totalLibros = (int)cmdLibros.ExecuteScalar();
            if (totalLibros > 0)
                return Results.BadRequest(new { mensaje = "No puedes eliminar esta categoría porque tiene libros asignados." });
        }

        string queryDelete = "delete from Categorias where id_categoria = @Id";
        using (SqlCommand cmdDelete = new SqlCommand(queryDelete, conn))
        {
            cmdDelete.Parameters.AddWithValue("@Id", id);
            int filas = cmdDelete.ExecuteNonQuery();
            if (filas == 0)
                return Results.NotFound(new { mensaje = "Categoría no encontrada." });
        }
    }

    return Results.Ok(new { mensaje = "Categoría eliminada correctamente." });
});




// --- ADMIN: Editar libro (precio, stock, etc.) ---
app.MapPut("/api/admin/libros/{id}", (string id, EditarLibroDto req) =>
{
    if (req.Precio < 0 || req.Stock < 0)
        return Results.BadRequest(new { mensaje = "El precio y el stock no pueden ser negativos." });

    string idLimpio = Uri.UnescapeDataString(id).Trim();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string queryUpdate = @"
            update Libros
            set precio = @Precio,
                stock = @Stock
            where id_libro = @Id";

        using (SqlCommand cmd = new SqlCommand(queryUpdate, conn))
        {
            cmd.Parameters.AddWithValue("@Id", idLimpio);
            cmd.Parameters.AddWithValue("@Precio", req.Precio);
            cmd.Parameters.AddWithValue("@Stock", req.Stock);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return Results.NotFound(new { mensaje = "Libro no encontrado." });
        }
    }

    return Results.Ok(new { mensaje = "Libro actualizado correctamente." });
});






// --- RUTA ACTUALIZADA: Ver Comentarios y Respuestas del Usuario ---

app.MapGet("/mis-comentarios/{correo}", (string correo) =>
{
    var listaComentarios = new List<object>();

    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string query = @"
                select id_mensaje, nombre, telefono, correo, mensaje, fecha_envio,
                       respuesta_admin, fecha_respuesta, isnull(leido_usuario, 0) as leido_usuario
                from Mensajes_Contacto
                where lower(ltrim(rtrim(correo))) = lower(ltrim(rtrim(@Correo)))
                order by fecha_envio desc, id_mensaje desc";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Correo", correo);
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        bool leido = false;
                        if (reader["leido_usuario"] != DBNull.Value)
                        {
                            leido = Convert.ToInt32(reader["leido_usuario"]) != 0;
                        }

                        listaComentarios.Add(new
                        {
                            idMensaje = reader["id_mensaje"],
                            nombre = reader["nombre"]?.ToString() ?? "",
                            telefono = reader["telefono"]?.ToString() ?? "",
                            correo = reader["correo"]?.ToString() ?? "",
                            mensaje = reader["mensaje"]?.ToString() ?? "",
                            fechaEnvio = reader["fecha_envio"] == DBNull.Value ? "" : Convert.ToDateTime(reader["fecha_envio"]).ToString("dd/MM/yyyy HH:mm"),
                            respuestaAdmin = reader["respuesta_admin"] == DBNull.Value ? null : reader["respuesta_admin"].ToString(),
                            fechaRespuesta = reader["fecha_respuesta"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_respuesta"]).ToString("dd/MM/yyyy HH:mm"),
                            leidoUsuario = leido
                        });
                    }
                }
            }
        }

        return Results.Ok(listaComentarios);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { mensaje = "Error en /mis-comentarios: " + ex.Message });
    }
});


// --- RUTA: Marcar respuesta como leída por el usuario ---
app.MapPut("/api/comentarios/{idMensaje:int}/leido", (int idMensaje) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "update Mensajes_Contacto set leido_usuario = 1 where id_mensaje = @Id";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Id", idMensaje);
            cmd.ExecuteNonQuery();
        }
    }
    return Results.Ok(new { mensaje = "Marcado como leído." });
});

// --- ADMIN: Listar todos los mensajes de contacto ---
app.MapGet("/api/admin/mensajes", () =>
{
    var mensajes = new List<object>();
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select id_mensaje, nombre, telefono, correo, mensaje, fecha_envio, respuesta_admin, fecha_respuesta
            from Mensajes_Contacto
            order by fecha_envio desc, id_mensaje desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                mensajes.Add(new
                {
                    idMensaje = Convert.ToInt32(reader["id_mensaje"]),
                    nombre = reader["nombre"] == DBNull.Value ? "Anónimo" : reader["nombre"].ToString(),
                    telefono = reader["telefono"] == DBNull.Value ? "" : reader["telefono"].ToString(),
                    correo = reader["correo"] == DBNull.Value ? "" : reader["correo"].ToString(),
                    mensaje = reader["mensaje"] == DBNull.Value ? "" : reader["mensaje"].ToString(),
                    fechaEnvio = reader["fecha_envio"] == DBNull.Value ? "" : Convert.ToDateTime(reader["fecha_envio"]).ToString("dd/MM/yyyy HH:mm"),
                    respuestaAdmin = reader["respuesta_admin"] == DBNull.Value ? null : reader["respuesta_admin"].ToString(),
                    fechaRespuesta = reader["fecha_respuesta"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_respuesta"]).ToString("dd/MM/yyyy HH:mm")
                });
            }
        }
    }
    return Results.Ok(mensajes);
});

// --- ADMIN: Responder a un mensaje ---
app.MapPut("/api/admin/mensajes/{idMensaje:int}/responder", (int idMensaje, RespuestaMensajeDto req) =>
{
    if (string.IsNullOrWhiteSpace(req.Respuesta))
        return Results.BadRequest(new { mensaje = "La respuesta no puede estar vacía." });

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            update Mensajes_Contacto
            set respuesta_admin = @Respuesta,
                fecha_respuesta = GETDATE(),
                leido_usuario = 0
            where id_mensaje = @Id";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Respuesta", req.Respuesta.Trim());
            cmd.Parameters.AddWithValue("@Id", idMensaje);
            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return Results.NotFound(new { mensaje = "Mensaje no encontrado." });
        }
    }
    return Results.Ok(new { mensaje = "Respuesta enviada correctamente." });
});








app.MapGet("/api/banner-popup", () =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select top 1 id_banner, imagen_url, enlace_url, activo
            from Banner_Popup
            order by id_banner desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                return Results.Ok(new
                {
                    idBanner = Convert.ToInt32(reader["id_banner"]),
                    imagenUrl = reader["imagen_url"] == DBNull.Value ? "" : reader["imagen_url"].ToString(),
                    enlaceUrl = reader["enlace_url"] == DBNull.Value ? "ofertas.html" : reader["enlace_url"].ToString(),
                    activo = reader["activo"] != DBNull.Value && Convert.ToBoolean(reader["activo"])
                });
            }
        }
    }

    return Results.Ok(new
    {
        idBanner = 0,
        imagenUrl = "",
        enlaceUrl = "ofertas.html",
        activo = false
    });
});

app.MapPut("/api/banner-popup", (GuardarBannerDto req) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string checkQuery = "select count(*) from Banner_Popup";
        int total;
        using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
        {
            total = Convert.ToInt32(checkCmd.ExecuteScalar());
        }

        if (total == 0)
        {
            string insertQuery = @"
                insert into Banner_Popup (imagen_url, enlace_url, activo, fecha_actualizacion)
                values (@ImagenUrl, @EnlaceUrl, @Activo, getdate())";

            using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@ImagenUrl", (object?)req.ImagenUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EnlaceUrl", string.IsNullOrWhiteSpace(req.EnlaceUrl) ? "ofertas.html" : req.EnlaceUrl.Trim());
                cmd.Parameters.AddWithValue("@Activo", req.Activo);
                cmd.ExecuteNonQuery();
            }
        }
        else
        {
            string updateQuery = @"
                update Banner_Popup
                set imagen_url = @ImagenUrl,
                    enlace_url = @EnlaceUrl,
                    activo = @Activo,
                    fecha_actualizacion = getdate()";

            using (SqlCommand cmd = new SqlCommand(updateQuery, conn))
            {
                cmd.Parameters.AddWithValue("@ImagenUrl", (object?)req.ImagenUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EnlaceUrl", string.IsNullOrWhiteSpace(req.EnlaceUrl) ? "ofertas.html" : req.EnlaceUrl.Trim());
                cmd.Parameters.AddWithValue("@Activo", req.Activo);
                cmd.ExecuteNonQuery();
            }
        }
    }

    return Results.Ok(new { mensaje = "Banner actualizado correctamente." });
});









app.MapGet("/api/banners-intermedios", () =>
{
    var lista = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select clave_banner, imagen_url, enlace_url, activo
            from Banners_Intermedios";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                lista.Add(new
                {
                    claveBanner = reader["clave_banner"].ToString(),
                    imagenUrl = reader["imagen_url"] == DBNull.Value ? "" : reader["imagen_url"].ToString(),
                    enlaceUrl = reader["enlace_url"] == DBNull.Value ? "" : reader["enlace_url"].ToString(),
                    activo = reader["activo"] != DBNull.Value && Convert.ToBoolean(reader["activo"])
                });
            }
        }
    }

    return Results.Ok(lista);
});

app.MapPut("/api/banners-intermedios/{clave}", (string clave, GuardarBannerIntermedioDto req) =>
{
    string claveLimpia = (clave ?? "").Trim().ToLower();
    if (claveLimpia != "contacto" && claveLimpia != "ofertas")
    {
        return Results.BadRequest(new { mensaje = "Clave de banner no válida." });
    }

    string enlaceDefecto = claveLimpia == "contacto" ? "contacto.html" : "ofertas.html";
    string enlaceFinal = string.IsNullOrWhiteSpace(req.EnlaceUrl) ? enlaceDefecto : req.EnlaceUrl.Trim();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string checkQuery = "select count(*) from Banners_Intermedios where clave_banner = @Clave";
        int existe;
        using (SqlCommand checkCmd = new SqlCommand(checkQuery, conn))
        {
            checkCmd.Parameters.AddWithValue("@Clave", claveLimpia);
            existe = Convert.ToInt32(checkCmd.ExecuteScalar());
        }

        if (existe == 0)
        {
            string insertQuery = @"
                insert into Banners_Intermedios (clave_banner, imagen_url, enlace_url, activo, fecha_actualizacion)
                values (@Clave, @ImagenUrl, @EnlaceUrl, @Activo, getdate())";

            using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Clave", claveLimpia);
                cmd.Parameters.AddWithValue("@ImagenUrl", (object?)req.ImagenUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EnlaceUrl", enlaceFinal);
                cmd.Parameters.AddWithValue("@Activo", req.Activo);
                cmd.ExecuteNonQuery();
            }
        }
        else
        {
            string updateQuery = @"
                update Banners_Intermedios
                set imagen_url = @ImagenUrl,
                    enlace_url = @EnlaceUrl,
                    activo = @Activo,
                    fecha_actualizacion = getdate()
                where clave_banner = @Clave";

            using (SqlCommand cmd = new SqlCommand(updateQuery, conn))
            {
                cmd.Parameters.AddWithValue("@Clave", claveLimpia);
                cmd.Parameters.AddWithValue("@ImagenUrl", (object?)req.ImagenUrl ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@EnlaceUrl", enlaceFinal);
                cmd.Parameters.AddWithValue("@Activo", req.Activo);
                cmd.ExecuteNonQuery();
            }
        }
    }

    return Results.Ok(new { mensaje = "Banner intermedio actualizado correctamente." });
});







app.MapGet("/api/admin/secciones-libros", () =>
{
    var lista = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select sl.id_seccion_libro, sl.seccion, sl.id_libro, sl.orden,
                   sl.precio_oferta, sl.descuento, sl.etiqueta, sl.descripcion,
                   l.Titulo, l.Autor, l.Precio, l.Imagen
            from Seccion_Libros sl
            inner join Libros l on l.id_libro = sl.id_libro
            order by sl.seccion, sl.orden, sl.id_seccion_libro desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        using (SqlDataReader reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                lista.Add(new
                {
                    idSeccionLibro = Convert.ToInt32(reader["id_seccion_libro"]),
                    seccion = reader["seccion"].ToString(),
                    idLibro = reader["id_libro"].ToString(),
                    orden = Convert.ToInt32(reader["orden"]),
                    precioOferta = reader["precio_oferta"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(reader["precio_oferta"]),
                    descuento = reader["descuento"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["descuento"]),
                    etiqueta = reader["etiqueta"] == DBNull.Value ? "" : reader["etiqueta"].ToString(),
                    descripcion = reader["descripcion"] == DBNull.Value ? "" : reader["descripcion"].ToString(),
                    titulo = reader["Titulo"].ToString(),
                    autor = reader["Autor"].ToString(),
                    precio = Convert.ToDecimal(reader["Precio"]),
                    imagen = reader["Imagen"] == DBNull.Value ? "" : reader["Imagen"].ToString()
                });
            }
        }
    }

    return Results.Ok(lista);
});

app.MapPost("/api/admin/secciones-libros", (AsignarLibroSeccionDto req) =>
{
    string seccion = (req.Seccion ?? "").Trim().ToLower();
    string idLibro = (req.IdLibro ?? "").Trim();

    if (seccion != "recomendaciones" &&
        seccion != "novedades" &&
        seccion != "pagina_novedades" &&
        seccion != "mas_buscados_quincena" &&
        seccion != "mas_vendido" &&
        seccion != "ofertas")
    {
        return Results.BadRequest(new { mensaje = "Sección no válida." });
    }

    if (string.IsNullOrWhiteSpace(idLibro))
    {
        return Results.BadRequest(new { mensaje = "Selecciona un libro." });
    }

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string existeQuery = @"
            select id_seccion_libro from Seccion_Libros
            where seccion = @Seccion and id_libro = @IdLibro";

        int? idExistente = null;
        using (SqlCommand existeCmd = new SqlCommand(existeQuery, conn))
        {
            existeCmd.Parameters.AddWithValue("@Seccion", seccion);
            existeCmd.Parameters.AddWithValue("@IdLibro", idLibro);
            var res = existeCmd.ExecuteScalar();
            if (res != null && res != DBNull.Value)
            {
                idExistente = Convert.ToInt32(res);
            }
        }

        if (idExistente.HasValue)
        {
            if (seccion == "ofertas" && (req.PrecioOferta.HasValue || req.Descuento.HasValue))
            {
                string updateOferta = @"
                    update Seccion_Libros
                    set precio_oferta = @PrecioOferta,
                        descuento = @Descuento
                    where id_seccion_libro = @Id";

                using (SqlCommand upCmd = new SqlCommand(updateOferta, conn))
                {
                    upCmd.Parameters.AddWithValue("@Id", idExistente.Value);
                    upCmd.Parameters.AddWithValue("@PrecioOferta", req.PrecioOferta.HasValue ? (object)req.PrecioOferta.Value : DBNull.Value);
                    upCmd.Parameters.AddWithValue("@Descuento", req.Descuento.HasValue ? (object)req.Descuento.Value : DBNull.Value);
                    upCmd.ExecuteNonQuery();
                }

                return Results.Ok(new { mensaje = "Oferta actualizada correctamente." });
            }

            if ((seccion == "novedades" || seccion == "pagina_novedades") &&
                (!string.IsNullOrWhiteSpace(req.Etiqueta) || !string.IsNullOrWhiteSpace(req.Descripcion)))
            {
                string updateNovedad = @"
                    update Seccion_Libros
                    set etiqueta = @Etiqueta,
                        descripcion = @Descripcion
                    where id_seccion_libro = @Id";

                using (SqlCommand upCmd = new SqlCommand(updateNovedad, conn))
                {
                    upCmd.Parameters.AddWithValue("@Id", idExistente.Value);
                    upCmd.Parameters.AddWithValue("@Etiqueta", string.IsNullOrWhiteSpace(req.Etiqueta) ? DBNull.Value : (object)req.Etiqueta.Trim());
                    upCmd.Parameters.AddWithValue("@Descripcion", string.IsNullOrWhiteSpace(req.Descripcion) ? DBNull.Value : (object)req.Descripcion.Trim());
                    upCmd.ExecuteNonQuery();
                }

                return Results.Ok(new { mensaje = "Novedad actualizada correctamente." });
            }

            return Results.BadRequest(new { mensaje = "Ese libro ya está asignado a esta sección." });
        }

        bool esNovedad = seccion == "novedades" || seccion == "pagina_novedades";

        string insertQuery = @"
            insert into Seccion_Libros (seccion, id_libro, orden, fecha_agregado, precio_oferta, descuento, etiqueta, descripcion)
            values (@Seccion, @IdLibro, 0, getdate(), @PrecioOferta, @Descuento, @Etiqueta, @Descripcion)";

        using (SqlCommand cmd = new SqlCommand(insertQuery, conn))
        {
            cmd.Parameters.AddWithValue("@Seccion", seccion);
            cmd.Parameters.AddWithValue("@IdLibro", idLibro);
            cmd.Parameters.AddWithValue("@PrecioOferta", seccion == "ofertas" && req.PrecioOferta.HasValue ? (object)req.PrecioOferta.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Descuento", seccion == "ofertas" && req.Descuento.HasValue ? (object)req.Descuento.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@Etiqueta", esNovedad && !string.IsNullOrWhiteSpace(req.Etiqueta) ? (object)req.Etiqueta.Trim() : DBNull.Value);
            cmd.Parameters.AddWithValue("@Descripcion", esNovedad && !string.IsNullOrWhiteSpace(req.Descripcion) ? (object)req.Descripcion.Trim() : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    string mensaje = seccion == "ofertas"
        ? "Libro agregado a ofertas."
        : seccion == "pagina_novedades"
            ? "Libro agregado a la página de Novedades."
            : seccion == "mas_buscados_quincena"
                ? "Libro agregado a Los Más Buscados de la Quincena."
                : seccion == "novedades"
                    ? "Libro agregado a novedades."
                    : "Libro agregado a la sección de inicio.";

    return Results.Ok(new { mensaje });
});

app.MapDelete("/api/admin/secciones-libros/{idSeccionLibro:int}", (int idSeccionLibro) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string deleteQuery = "delete from Seccion_Libros where id_seccion_libro = @Id";
        using (SqlCommand cmd = new SqlCommand(deleteQuery, conn))
        {
            cmd.Parameters.AddWithValue("@Id", idSeccionLibro);
            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
            {
                return Results.NotFound(new { mensaje = "Registro no encontrado." });
            }
        }
    }

    return Results.Ok(new { mensaje = "Libro quitado de la sección." });
});

// --- 2. INICIAR LA APLICACIÓN ---
app.Run();

record PerfilUpdateDto(
    string Nombre,
    string? Apellido,
    string? Contrasena,
    string? Telefono,
    string? Direccion,
    string? Ciudad,
    string? CodigoPostal
);



record NuevoLibroAdminDto(string Titulo, string Autor, decimal Precio, int Stock, string? Imagen, int IdCategoria);
record EstadoPedidoDto(string Estado);

record NuevaCategoriaDto(string Nombre);

record EditarLibroDto(decimal Precio, int Stock);

record RespuestaMensajeDto(string Respuesta);

record GuardarBannerDto(string? ImagenUrl, string? EnlaceUrl, bool Activo);

record GuardarBannerIntermedioDto(string? ImagenUrl, string? EnlaceUrl, bool Activo);

record AsignarLibroSeccionDto(
    string? Seccion,
    string? IdLibro,
    decimal? PrecioOferta = null,
    int? Descuento = null,
    string? Etiqueta = null,
    string? Descripcion = null
);

// --- 3. EL RECORD SIEMPRE VA HASTA ABAJO DE TODO EL ARCHIVO ---
public record CambioPasswordRequest(string Correo, string NuevaPassword);

// ==========================================
// CLASES AUXILIARES (Data Transfer Objects)
// ==========================================

public class UsuarioRegistro
{
    public string Nombre { get; set; }
    public string Apellido { get; set; }
    public string Correo { get; set; }
    public string Contrasena { get; set; }
    public string Telefono { get; set; }
}

public class UsuarioLogin
{
    public string Correo { get; set; }
    public string Contrasena { get; set; }
}

public class CheckoutRequest
{
    public int IdUsuario { get; set; }
    public decimal Total { get; set; }
    public string Direccion { get; set; }
    public string Ciudad { get; set; }
    public string CodigoPostal { get; set; }
    public string TelefonoEnvio { get; set; }
    public List<ItemCarrito> Carrito { get; set; }
}

public class ItemCarrito
{
    public string IdLibro { get; set; }
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
}

public class ContactoRequest
{
    public string Nombre { get; set; }
    public string Telefono { get; set; }
    public string Correo { get; set; }
    public string Mensaje { get; set; }
}