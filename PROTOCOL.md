# Altair AP-3000 Installation Projector — Control Protocol (Real)

do not axepted \x0d or \x0a in the comments, first message axepted but next spoiled with \x0d or \x0a
axepted several command in one packege like this - SYS:?;SRC:?;LGT:?;

device reaction to the command from 50 - 270 ms

device write evrithing it got to the buffer and try to execute it only after reciving delimiter - ";"

got !ID:AP-3000:1.07 after connected to the device, probably 1.07 is the FW

power state can be 
0 - device off
1 - device on
3 - device switching on - 8 sec
4 - device switching off - 5 sec

got !RDY\x0d\x0a when system is on
got !STBY\x0d\x0a when system is off

SRC:?; - got NAK:30\x0d\x0a if device is off

LGT:? - fb 0-255, but set is 0 - 100

!HB -> HB; - no response + ignored HB; before !HB
any command exapt HB; do not reset the hb timer
between !HB  20 - 25 sec, after resiving !HB have 5sec for HB; response 
if missing 2 HB -> disconect

got !DROP\x0d\x0a and close connection when no HB;

Device suppoer only one connection, if attempted to connect second client, device return 
!DENY\x0d\x0a
and close the connection.

when device is on, reciving not documented commands
!SYNC:4:1\x0d\x0a
!SYNC:3:0\x0d\x0a
!SYNC:3:0\x0d\x0a

device ignor small % of commands, if threre no any response to the command driver repeat the command again

Source cant be readed form device or changed if device is off




