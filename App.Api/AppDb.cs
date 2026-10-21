using Microsoft.EntityFrameworkCore;

namespace App.Api;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
}
