using Conductor.Model;
using Conductor.Service;

namespace Conductor.Controller;

public sealed class OriginController(OriginService service) : ControllerBase<Origin>(service) { }