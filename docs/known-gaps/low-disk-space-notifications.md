# Low disk space notifications


**Status:** the 26.3 `low_disk_space_warning` packet is available, but the server does not monitor free disk space.
Vanilla's dedicated-server disk-space check has no corresponding Obsidian trigger. Add a rate-limited storage
monitor before sending this warning; a normal save failure alone does not establish that the disk is nearly full.
