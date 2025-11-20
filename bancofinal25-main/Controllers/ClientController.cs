using System;
using System.Linq;
using System.Threading.Tasks;
using AtlasAir.Attributes;
using AtlasAir.Interfaces;
using AtlasAir.Models;
using AtlasAir.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AtlasAir.Controllers
{
    [ClientOnly]
    public class ClientController : Controller
    {
        private readonly IFlightRepository _flightRepository;
        private readonly ISeatRepository _seatRepository;
        private readonly IReservationRepository _reservationRepository;
        private readonly IAirportRepository _airportRepository;

        public ClientController(
            IFlightRepository flightRepository,
            ISeatRepository seatRepository,
            IReservationRepository reservationRepository,
            IAirportRepository airportRepository)
        {
            _flightRepository = flightRepository ?? throw new ArgumentNullException(nameof(flightRepository));
            _seatRepository = seatRepository ?? throw new ArgumentNullException(nameof(seatRepository));
            _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
            _airportRepository = airportRepository ?? throw new ArgumentNullException(nameof(airportRepository));
        }

        // Mantém /Client funcionando: redireciona para Search
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Search));
        }

        // Página de busca — agora retorna diretamente a lista de voos para a view
        [HttpGet]
        public async Task<IActionResult> Search()
        {
            var flights = await _flightRepository.GetAllAsync() ?? Enumerable.Empty<Flight>();
            // garanta que OriginAirport e DestinationAirport estejam carregados no repositório (Include)
            return View(flights);
        }

        // Endpoint AJAX: retorna voos por rota (mantive caso seja usado por outras telas)
        [HttpGet]
        public async Task<IActionResult> GetFlightsByRoute(int originId, int destinationId)
        {
            var flights = Enumerable.Empty<Flight>();
            try
            {
                var method = _flightRepository.GetType().GetMethod("GetFlightsByRouteAsync");
                if (method != null)
                {
                    var task = (Task)method.Invoke(_flightRepository, new object[] { originId, destinationId });
                    await task.ConfigureAwait(false);
                    var resultProp = task.GetType().GetProperty("Result");
                    flights = (IEnumerable<Flight>)resultProp?.GetValue(task) ?? Enumerable.Empty<Flight>();
                }
                else
                {
                    flights = (await _flightRepository.GetAllAsync())?
                        .Where(f => f.OriginAirportId == originId && f.DestinationAirportId == destinationId)
                        ?? Enumerable.Empty<Flight>();
                }
            }
            catch
            {
                flights = (await _flightRepository.GetAllAsync())?
                    .Where(f => f.OriginAirportId == originId && f.DestinationAirportId == destinationId)
                    ?? Enumerable.Empty<Flight>();
            }

            var data = flights.Select(f => new
            {
                f.Id,
                Origin = f.OriginAirport?.Name,
                Destination = f.DestinationAirport?.Name,
                Departure = f.ScheduledDeparture.ToString("g"),
                Arrival = f.ScheduledArrival.ToString("g")
            });

            return Json(data);
        }

        // Página que mostra assentos de um voo e permite selecionar
        [HttpGet]
        public async Task<IActionResult> Seats(int id)
        {
            var flight = await _flightRepository.GetByIdAsync(id);
            if (flight == null) return NotFound();

            var seats = await _seatRepository.GetAvailableSeatsByFlightIdAsync(id) ?? Enumerable.Empty<Seat>();

            ViewData["Flight"] = flight;
            return View(seats);
        }

        // Compra
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Purchase(int flightId, int seatId)
        {
            var customerId = HttpContext.Session.GetInt32("CustomerId");
            if (!customerId.HasValue)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Seats", "Client", new { id = flightId }) });
            }

            var reservation = new Reservation
            {
                ReservationCode = $"R-{Guid.NewGuid().ToString().Split('-')[0].ToUpper()}",
                CustomerId = customerId.Value,
                FlightId = flightId,
                SeatId = seatId,
                ReservationDate = DateTime.UtcNow,
                Status = AtlasAir.Enums.ReservationStatus.Confirmed
            };

            await _reservationRepository.CreateAsync(reservation);
            TempData["SuccessMessage"] = $"Reserva criada: {reservation.ReservationCode}";
            return RedirectToAction("DashboardTest");
        }

        // Página simples de teste para cliente
        public IActionResult DashboardTest()
        {
            return View();
        }

        // GET: mostra tela de pagamento de teste
        [HttpGet]
        public async Task<IActionResult> Payment(int flightId, int seatId)
        {
            var flight = await _flightRepository.GetByIdAsync(flightId);
            var seat = await _seatRepository.GetByIdAsync(seatId);

            if (flight == null || seat == null) return NotFound();

            var vm = new PaymentViewModel
            {
                FlightId = flightId,
                SeatId = seatId,
                Flight = flight,
                Seat = seat,
                Price = 199.90m
            };

            return View(vm);
        }

        // POST: recebe dados do form de pagamento (fake), cria reserva e redireciona para confirmação
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Payment(PaymentFormModel form)
        {
            if (!ModelState.IsValid)
            {
                // recarrega view com dados mínimos
                var flight = await _flightRepository.GetByIdAsync(form.FlightId);
                var seat = await _seatRepository.GetByIdAsync(form.SeatId);
                var vm = new PaymentViewModel
                {
                    FlightId = form.FlightId,
                    SeatId = form.SeatId,
                    Flight = flight,
                    Seat = seat,
                    Price = 199.90m
                };
                return View(vm);
            }

            var customerId = HttpContext.Session.GetInt32("CustomerId");
            if (!customerId.HasValue)
            {
                return RedirectToAction("Login", "Account", new { returnUrl = Url.Action("Seats", "Client", new { id = form.FlightId }) });
            }

            var reservation = new Reservation
            {
                ReservationCode = $"R-{Guid.NewGuid().ToString().Split('-')[0].ToUpper()}",
                CustomerId = customerId.Value,
                FlightId = form.FlightId,
                SeatId = form.SeatId,
                ReservationDate = DateTime.UtcNow,
                Status = AtlasAir.Enums.ReservationStatus.Confirmed
            };

            await _reservationRepository.CreateAsync(reservation);

            // redireciona para confirmação de pagamento
            return RedirectToAction(nameof(PaymentConfirmation), new { code = reservation.ReservationCode });
        }

        [HttpGet]
        public IActionResult PaymentConfirmation(string code)
        {
            ViewBag.ReservationCode = code;
            return View();
        }
    }
}