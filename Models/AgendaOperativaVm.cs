using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ERP.NSQuell.Models
{
    public class AgendaOperativaVm : Controller
    {
        // GET: AgendaOperativaVm
        public ActionResult Index()
        {
            return View();
        }

        // GET: AgendaOperativaVm/Details/5
        public ActionResult Details(int id)
        {
            return View();
        }

        // GET: AgendaOperativaVm/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: AgendaOperativaVm/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: AgendaOperativaVm/Edit/5
        public ActionResult Edit(int id)
        {
            return View();
        }

        // POST: AgendaOperativaVm/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: AgendaOperativaVm/Delete/5
        public ActionResult Delete(int id)
        {
            return View();
        }

        // POST: AgendaOperativaVm/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
