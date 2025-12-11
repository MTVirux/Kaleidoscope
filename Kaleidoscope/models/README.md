Models for Kaleidoscope
=======================

This folder contains simple POCO models used to represent data received from
ECommons and FFXIVClientStructs. The intent is to keep these models dependency-free
so mapping code in the integration layer can convert from generated/native types
into these lightweight types.

Files
- `GameModels.cs` : Contains `Position`, `InventoryItemModel`, `ActorModel`, `PlayerModel`, and `AddressResolutionModel`.

Mapping notes
- Keep mapping logic in a separate integration class (for example, an `Mappers` class)
  that references `FFXIVClientStructs` and `ECommons` types. That prevents generated
  native types from leaking into the models layer and keeps the models testable.

Suggested next steps
- Add a `Kaleidoscope/Integration/Mappers.cs` to implement conversions from
  `FFXIVClientStructs` types to these POCOs (optional — I can create it if you want).
