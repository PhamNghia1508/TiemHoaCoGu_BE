using HoaSap.BLL.Commerce;
using HoaSap.DAL;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HoaSap.API.Controllers;

[ApiController, Route("api/catalog")]
public sealed class CatalogController(CommerceService service, HoaSapDbContext db) : ControllerBase
{
    [HttpGet("products")]
    public Task<PageResult<ProductDto>> Products([FromQuery] CatalogQuery query, CancellationToken ct) => service.CatalogAsync(query, ct);

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await db.Categories.AsNoTracking().OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.Code, x.Name, x.Description }).ToListAsync(ct));

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> Product(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Include(x => x.Images).Include(x => x.Inventory)
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, ct);
        if (product is null) return NotFound();
        return Ok(new
        {
            product.Id,
            product.CategoryId,
            product.Code,
            product.Name,
            product.Description,
            product.Price,
            product.OriginalPrice,
            product.ThumbnailUrl,
            product.Badge,
            product.Section,
            product.IsCustomizable,
            product.IsPreOrder,
            quantityAvailable = product.Inventory?.QuantityAvailable ?? 0,
            images = product.Images.OrderBy(x => x.DisplayOrder).Select(x => new { x.Id, x.ImageUrl, x.DisplayOrder })
        });
    }
}
