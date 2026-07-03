Library for reading/writing file types from the Diesel Engine

I may do some bits here and there in the future. Feel free to fork this repo or make Pull Requests. Code style is kinda all over the place, but I would prefer keeping it to CamelCase.

# Supported File Types

| File Type | Read | Write |
| --- | --- | --- |
| massunit | x | x |
| strings | x |  |
| font | x (BMFont & binary) | x |
| banksinfo | x | |
| blb | x |  |
| bundle | x | x |
| script files* | x | x |
| bnk | x | x |
| crate | x | |
| crates.shipping_manifest | x | |
| crates.properties | x | |

Bundle/blb/Script file support should work for PD:TH/PD2/PD2 Linux files

## Script Files (May be incomplete)
* sequence_manager
* environment
* menu
* continent
* continents
* mission
* nav_data
* cover_data
* world
* world_cameras
* prefhud
* objective
* credits
* hint
* comment
* dialog
* dialog_index
* timeline
* action_message
* achievment
* controller_settings

# Credits

A lot of the code is based on work by Zwagoth and I am not a spy..., much of the code has been changed a lot, but it would have been a lot harder without their work that can be found [here](https://bitbucket.org/zabb65/payday-2-modding-information)
