using Conductor.Model;
using Conductor.Service;

namespace Conductor.Controller;

public sealed class DestinationController(DestinationService service) : ControllerBase<Destination>(service) { }