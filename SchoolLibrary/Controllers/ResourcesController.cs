
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Data;

public class ResourcesController : Controller
{
    private readonly ApplicationDbContext _context;

    public ResourcesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET: RESOURCES
    public async Task<IActionResult> Index()    
    {
        return View(await _context.Resources.ToListAsync());
    }

    // GET: RESOURCES/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var resource = await _context.Resources
            .FirstOrDefaultAsync(m => m.Id == id);
        if (resource == null)
        {
            return NotFound();
        }

        return View(resource);
    }

    // GET: RESOURCES/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: RESOURCES/Create
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Id,Title,Description,Url,CreatedOn,CategoryId,Category,GradeLevelId,GradeLevel,OwnerId,Owner")] Resource resource)
    {
        if (ModelState.IsValid)
        {
            _context.Add(resource);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        return View(resource);
    }

    // GET: RESOURCES/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var resource = await _context.Resources.FindAsync(id);
        if (resource == null)
        {
            return NotFound();
        }
        return View(resource);
    }

    // POST: RESOURCES/Edit/5
    // To protect from overposting attacks, enable the specific properties you want to bind to.
    // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int? id, [Bind("Id,Title,Description,Url,CreatedOn,CategoryId,Category,GradeLevelId,GradeLevel,OwnerId,Owner")] Resource resource)
    {
        if (id != resource.Id)
        {
            return NotFound();
        }

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(resource);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ResourceExists(resource.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
            return RedirectToAction(nameof(Index));
        }
        return View(resource);
    }

    // GET: RESOURCES/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var resource = await _context.Resources
            .FirstOrDefaultAsync(m => m.Id == id);
        if (resource == null)
        {
            return NotFound();
        }

        return View(resource);
    }

    // POST: RESOURCES/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int? id)
    {
        var resource = await _context.Resources.FindAsync(id);
        if (resource != null)
        {
            _context.Resources.Remove(resource);
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool ResourceExists(int? id)
    {
        return _context.Resources.Any(e => e.Id == id);
    }
}
