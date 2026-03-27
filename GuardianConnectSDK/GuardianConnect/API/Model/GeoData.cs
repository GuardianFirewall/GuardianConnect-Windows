//using Newtonsoft.Json;

using System.Text.Json.Serialization;

// ReSharper disable CollectionNeverUpdated.Global
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace GuardianConnect.API.Model;
#pragma warning disable 0649
public class GeoData
{
    public static readonly List<GeoData> StaticGeoDataCollection = new()
    {
        new GeoData
        {
            KeyName = "eu-gr",
            DisplayName = "Greece",
            Continent = "Europe",
            Countryisocode = "GR",
            Timezones = new List<string>
            {
                "Europe/Athens"
            }
        },
        new GeoData
        {
            KeyName = "asia-il",
            DisplayName = "Israel",
            Continent = "Asia",
            Countryisocode = "IL",
            Timezones = new List<string>
            {
                "Asia/Jerusalem"
            }
        },
        new GeoData
        {
            KeyName = "eu-es",
            DisplayName = "Spain",
            Continent = "Europe",
            Countryisocode = "ES",
            Timezones = new List<string>
            {
                "Europe/Madrid",
                "Europe/Gibraltar",
                "Africa/Casablanca",
                "Africa/Algiers",
                "Africa/El_Aaiun",
                "Africa/Tunis",
                "Africa/Ceuta",
                "Atlantic/Canary"
            }
        },
        new GeoData
        {
            KeyName = "eu-pl",
            DisplayName = "Poland",
            Continent = "Europe",
            Countryisocode = "PL",
            Timezones = new List<string>
            {
                "Europe/Warsaw",
                "Europe/Vilnius",
                "Europe/Volgograd",
                "Europe/Kaliningrad",
                "Europe/Kirov",
                "Europe/Samara",
                "Europe/Saratov",
                "Europe/Tallinn",
                "Europe/Ulyanovsk",
                "Europe/Minsk",
                "Europe/Moscow",
                "Europe/Riga"
            }
        },
        new GeoData
        {
            KeyName = "eu-fr",
            DisplayName = "France",
            Continent = "Europe",
            Countryisocode = "FR",
            Timezones = new List<string>
            {
                "Africa/Blantyre",
                "Africa/Brazzaville",
                "Africa/Bujumbura",
                "Africa/Cairo",
                "Africa/Douala",
                "Africa/Tripoli",
                "Atlantic/Azores",
                "Atlantic/Cape_Verde",
                "Atlantic/Madeira",
                "Europe/Paris",
                "Europe/Andorra",
                "Europe/Guernsey",
                "Europe/Jersey",
                "Europe/Monaco"
            }
        },
        new GeoData
        {
            KeyName = "us-west",
            DisplayName = "USA (West)",
            Continent = "North-America",
            Countryisocode = "US",
            Timezones = new List<string>
            {
                "America/Phoenix",
                "America/Shiprock",
                "America/Sitka",
                "America/Yakutat",
                "America/Adak",
                "America/Anchorage",
                "America/Bahia_Banderas",
                "America/Boise",
                "America/El_Salvador",
                "Pacific/Honolulu",
                "Pacific/Johnston",
                "Pacific/Kiritimati",
                "Pacific/Midway",
                "America/Juneau",
                "America/Los_Angeles",
                "America/Metlakatla",
                "America/Nome"
            }
        },
        new GeoData
        {
            KeyName = "eu-ie",
            DisplayName = "Ireland",
            Continent = "Europe",
            Countryisocode = "IE",
            Timezones = new List<string>
            {
                "Europe/Isle_of_Man",
                "Europe/Dublin"
            }
        },
        new GeoData
        {
            KeyName = "eu-en",
            DisplayName = "United Kingdom",
            Continent = "Europe",
            Countryisocode = "GB",
            Timezones = new List<string>
            {
                "Atlantic/Faroe",
                "Atlantic/Reykjavik",
                "Europe/London",
                "Asia/Riyadh"
            }
        },
        new GeoData
        {
            KeyName = "eu-cr",
            DisplayName = "Croatia",
            Continent = "Europe",
            Countryisocode = "HR",
            Timezones = new List<string>
            {
                "Europe/Zagreb",
                "Europe/Belgrade",
                "Europe/Podgorica",
                "Europe/Ljubljana",
                "Europe/Sarajevo",
                "Europe/Skopje",
                "Europe/Tirane"
            }
        },
        new GeoData
        {
            KeyName = "asia-sg",
            DisplayName = "Singapore",
            Continent = "Asia",
            Countryisocode = "SG",
            Timezones = new List<string>
            {
                "Asia/Aden",
                "Asia/Almaty",
                "Asia/Amman",
                "Asia/Anadyr",
                "Asia/Aqtau",
                "Asia/Aqtobe",
                "Asia/Ashgabat",
                "Asia/Atyrau",
                "Asia/Baghdad",
                "Asia/Bahrain",
                "Asia/Baku",
                "Asia/Bangkok",
                "Asia/Barnaul",
                "Asia/Beirut",
                "Asia/Bishkek",
                "Asia/Brunei",
                "Asia/Calcutta",
                "Asia/Chita",
                "Asia/Choibalsan",
                "Asia/Colombo",
                "Asia/Damascus",
                "Asia/Dhaka",
                "Asia/Dili",
                "Asia/Dubai",
                "Asia/Dushanbe",
                "Asia/Famagusta",
                "Asia/Gaza",
                "Asia/Harbin",
                "Asia/Hebron",
                "Asia/Ho_Chi_Minh",
                "Asia/Hong_Kong",
                "Asia/Hovd",
                "Asia/Irkutsk",
                "Asia/Jakarta",
                "Asia/Jayapura",
                "Asia/Kabul",
                "Asia/Kamchatka",
                "Asia/Karachi",
                "Asia/Kashgar",
                "Asia/Kathmandu",
                "Asia/Katmandu",
                "Asia/Khandyga",
                "Asia/Krasnoyarsk",
                "Asia/Kuala_Lumpur",
                "Asia/Kuching",
                "Asia/Macau",
                "Asia/Magadan",
                "Indian/Chagos",
                "Indian/Christmas",
                "Indian/Cocos",
                "Indian/Comoro",
                "Indian/Kerguelen",
                "Indian/Mahe",
                "Indian/Maldives",
                "Indian/Mauritius",
                "Indian/Mayotte",
                "Indian/Reunion",
                "Pacific/Fakaofo",
                "Pacific/Palau",
                "Asia/Chongqing",
                "Asia/Makassar",
                "Asia/Manila",
                "Asia/Muscat",
                "Asia/Nicosia",
                "Asia/Novokuznetsk",
                "Asia/Novosibirsk",
                "Asia/Omsk",
                "Asia/Oral",
                "Asia/Phnom_Penh",
                "Asia/Pontianak",
                "Asia/Pyongyang",
                "Asia/Qostanay",
                "Asia/Qyzylorda",
                "Asia/Rangoon",
                "Asia/Sakhalin",
                "Asia/Samarkand",
                "Asia/Seoul",
                "Asia/Shanghai",
                "Asia/Singapore",
                "Asia/Srednekolymsk",
                "Asia/Taipei",
                "Asia/Tashkent",
                "Asia/Tbilisi",
                "Asia/Tehran",
                "Asia/Thimphu",
                "Asia/Tomsk",
                "Asia/Ulaanbaatar",
                "Asia/Urumqi",
                "Asia/Ust-Nera",
                "Asia/Vientiane",
                "Asia/Vladivostok",
                "Asia/Yakutsk",
                "Asia/Yangon",
                "Asia/Yekaterinburg",
                "Asia/Yerevan",
                "Asia/Kolkata",
                "Asia/Saigon"
            }
        },
        new GeoData
        {
            KeyName = "eu-nl",
            DisplayName = "Netherlands",
            Continent = "Europe",
            Countryisocode = "NL",
            Timezones = new List<string>
            {
                "Europe/Amsterdam"
            }
        },
        new GeoData
        {
            KeyName = "us-east",
            DisplayName = "USA (East)",
            Continent = "North-America",
            Countryisocode = "US",
            Timezones = new List<string>
            {
                "America/Indiana/Indianapolis",
                "Atlantic/Bermuda",
                "America/Anguilla",
                "America/Antigua",
                "America/Santa_Isabel",
                "America/Santo_Domingo",
                "America/Indiana/Knox",
                "America/Indiana/Marengo",
                "America/Indiana/Petersburg",
                "America/Indiana/Tell_City",
                "America/Indiana/Vevay",
                "America/Indiana/Vincennes",
                "America/Indiana/Winamac",
                "America/Kentucky/Louisville",
                "America/Kentucky/Monticello",
                "America/New_York",
                "America/Rio_Branco",
                "America/Paramaribo",
                "America/Port_of_Spain",
                "Atlantic/Stanley",
                "America/Asuncion",
                "America/Bogota",
                "America/Manaus",
                "America/Cayenne",
                "America/La_Paz",
                "America/Caracas",
                "America/Lima",
                "America/Indianapolis"
            }
        },
        new GeoData
        {
            KeyName = "eu-at",
            DisplayName = "Austria",
            Continent = "Europe",
            Countryisocode = "AT",
            Timezones = new List<string>
            {
                "Europe/Vienna",
                "Europe/Budapest",
                "Europe/Bratislava"
            }
        },
        new GeoData
        {
            KeyName = "eu-de",
            DisplayName = "Germany",
            Continent = "Europe",
            Countryisocode = "DE",
            Timezones = new List<string>
            {
                "Asia/Kuwait",
                "Europe/Astrakhan",
                "Europe/Berlin",
                "Europe/Busingen",
                "Europe/Helsinki",
                "Europe/Istanbul",
                "Europe/Luxembourg",
                "Europe/Mariehamn",
                "Europe/Oslo",
                "Europe/Stockholm",
                "Asia/Qatar"
            }
        },
        new GeoData
        {
            KeyName = "au-au",
            DisplayName = "Australia",
            Continent = "Oceania",
            Countryisocode = "AU",
            Timezones = new List<string>
            {
                "Australia/Adelaide",
                "Australia/Brisbane",
                "Australia/Broken_Hill",
                "Australia/Currie",
                "Australia/Darwin",
                "Australia/Eucla",
                "Australia/Hobart",
                "Australia/Lindeman",
                "Australia/Lord_Howe",
                "Australia/Melbourne",
                "Australia/Perth",
                "Australia/Sydney",
                "Pacific/Apia",
                "Pacific/Bougainville",
                "Pacific/Chatham",
                "Pacific/Chuuk",
                "Pacific/Easter",
                "Pacific/Efate",
                "Pacific/Enderbury",
                "Pacific/Fiji",
                "Pacific/Funafuti",
                "Pacific/Gambier",
                "Pacific/Guadalcanal",
                "Pacific/Kosrae",
                "Pacific/Kwajalein",
                "Pacific/Majuro",
                "Pacific/Marquesas",
                "Pacific/Nauru",
                "Pacific/Niue",
                "Pacific/Norfolk",
                "Pacific/Noumea",
                "Pacific/Pago_Pago",
                "Pacific/Pitcairn",
                "Pacific/Pohnpei",
                "Pacific/Ponape",
                "Pacific/Port_Moresby",
                "Pacific/Rarotonga",
                "Pacific/Tahiti",
                "Pacific/Tarawa",
                "Pacific/Tongatapu",
                "Pacific/Truk",
                "Pacific/Wake",
                "Pacific/Wallis"
            }
        },
        new GeoData
        {
            KeyName = "us-central",
            DisplayName = "USA (Central)",
            Continent = "North-America",
            Countryisocode = "US",
            Timezones = new List<string>
            {
                "America/Grand_Turk",
                "America/Grenada",
                "America/Guadeloupe",
                "America/Guayaquil",
                "America/Guyana",
                "America/Havana",
                "Antarctica/Casey",
                "Antarctica/Davis",
                "Antarctica/DumontDUrville",
                "Antarctica/Macquarie",
                "Antarctica/Mawson",
                "Antarctica/McMurdo",
                "Antarctica/Palmer",
                "Antarctica/Rothera",
                "Antarctica/South_Pole",
                "Antarctica/Syowa",
                "Antarctica/Troll",
                "Antarctica/Vostok",
                "Arctic/Longyearbyen",
                "America/Ojinaga",
                "America/Panama",
                "America/Port-au-Prince",
                "America/Porto_Velho",
                "America/Puerto_Rico",
                "America/Recife",
                "America/St_Barthelemy",
                "America/St_Kitts",
                "America/St_Lucia",
                "America/St_Thomas",
                "Atlantic/South_Georgia",
                "Atlantic/St_Helena",
                "America/Araguaina",
                "America/Aruba",
                "America/Bahia",
                "America/Barbados",
                "Pacific/Galapagos",
                "America/Belize",
                "America/Boa_Vista",
                "America/Campo_Grande",
                "America/Cayman",
                "America/Chicago",
                "America/Chihuahua",
                "America/Costa_Rica",
                "America/Cuiaba",
                "America/Curacao",
                "America/Detroit",
                "America/Dominica",
                "America/Eirunepe",
                "America/Jamaica",
                "America/Kralendijk",
                "America/Lower_Princes",
                "America/Maceio",
                "America/Marigot",
                "America/Martinique",
                "America/Matamoros",
                "America/Mazatlan",
                "America/Menominee",
                "America/Merida",
                "America/Montserrat",
                "America/Nassau",
                "America/Noronha",
                "America/North_Dakota/Beulah",
                "America/North_Dakota/Center",
                "America/North_Dakota/New_Salem",
                "America/St_Vincent",
                "America/Tortola",
                "Etc/GMT+3"
            }
        },
        new GeoData
        {
            KeyName = "nz-nz",
            DisplayName = "New Zealand",
            Continent = "Oceania",
            Countryisocode = "NZ",
            Timezones = new List<string>
            {
                "Pacific/Auckland"
            }
        },
        new GeoData
        {
            KeyName = "ca-east",
            DisplayName = "Canada",
            Continent = "North-America",
            Countryisocode = "CA",
            Timezones = new List<string>
            {
                "America/Atikokan",
                "America/Glace_Bay",
                "America/Godthab",
                "America/Goose_Bay",
                "America/Halifax",
                "America/Pangnirtung",
                "America/Rainy_River",
                "America/Rankin_Inlet",
                "America/Regina",
                "America/Resolute",
                "America/Scoresbysund",
                "America/St_Johns",
                "America/Swift_Current",
                "America/Thule",
                "America/Thunder_Bay",
                "America/Toronto",
                "America/Vancouver",
                "America/Whitehorse",
                "America/Winnipeg",
                "America/Yellowknife",
                "America/Blanc-Sablon",
                "America/Cambridge_Bay",
                "America/Creston",
                "America/Danmarkshavn",
                "America/Dawson",
                "America/Dawson_Creek",
                "America/Edmonton",
                "America/Fort_Nelson",
                "America/Inuvik",
                "America/Iqaluit",
                "America/Miquelon",
                "America/Moncton",
                "America/Montreal",
                "America/Nipigon",
                "America/Nuuk"
            }
        },
        new GeoData
        {
            KeyName = "eu-dk",
            DisplayName = "Denmark",
            Continent = "Europe",
            Countryisocode = "DK",
            Timezones = new List<string>
            {
                "Europe/Copenhagen"
            }
        },
        new GeoData
        {
            KeyName = "eu-ch",
            DisplayName = "Switzerland",
            Continent = "Europe",
            Countryisocode = "CH",
            Timezones = new List<string>
            {
                "Europe/Zurich",
                "Europe/Vaduz"
            }
        },
        new GeoData
        {
            KeyName = "eu-ro",
            DisplayName = "Romania",
            Continent = "Europe",
            Countryisocode = "RO",
            Timezones = new List<string>
            {
                "Europe/Bucharest",
                "Europe/Sofia"
            }
        },
        new GeoData
        {
            KeyName = "us-mountain",
            DisplayName = "USA (Mountain)",
            Continent = "North-America",
            Countryisocode = "US",
            Timezones = new List<string>
            {
                "America/Denver"
            }
        },
        new GeoData
        {
            KeyName = "eu-pt",
            DisplayName = "Portugal",
            Continent = "Europe",
            Countryisocode = "PT",
            Timezones = new List<string>
            {
                "Europe/Lisbon"
            }
        },
        new GeoData
        {
            KeyName = "eu-ua",
            DisplayName = "Ukraine",
            Continent = "Europe",
            Countryisocode = "UA",
            Timezones = new List<string>
            {
                "Europe/Zaporozhye",
                "Europe/Kiev",
                "Europe/Chisinau",
                "Europe/Simferopol",
                "Europe/Uzhgorod"
            }
        },
        new GeoData
        {
            KeyName = "eu-cz",
            DisplayName = "Czech-Republic",
            Continent = "Europe",
            Countryisocode = "CZ",
            Timezones = new List<string>
            {
                "Europe/Prague"
            }
        },
        new GeoData
        {
            KeyName = "af-za",
            DisplayName = "South Africa",
            Continent = "Africa",
            Countryisocode = "ZA",
            Timezones = new List<string>
            {
                "Africa/Johannesburg",
                "Africa/Windhoek",
                "Africa/Gaborone",
                "Africa/Maputo",
                "Africa/Mbabane",
                "Africa/Maseru",
                "Africa/Harare",
                "Africa/Lusaka",
                "Africa/Lubumbashi",
                "Africa/Luanda",
                "Africa/Kinshasa",
                "Africa/Dar_es_Salaam",
                "Africa/Kigali",
                "Africa/Kampala",
                "Africa/Nairobi",
                "Africa/Mogadishu",
                "Africa/Juba",
                "Africa/Addis_Ababa",
                "Africa/Djibouti",
                "Africa/Asmara",
                "Africa/Khartoum",
                "Africa/Bangui",
                "Africa/Ndjamena",
                "Africa/Libreville",
                "Africa/Sao_Tome",
                "Africa/Malabo",
                "Africa/Lagos",
                "Africa/Lome",
                "Africa/Accra",
                "Africa/Abidjan",
                "Africa/Monrovia",
                "Africa/Porto-Novo",
                "Africa/Niamey",
                "Africa/Ouagadougou",
                "Africa/Bamako",
                "Africa/Freetown",
                "Africa/Conakry",
                "Africa/Bissau",
                "Africa/Banjul",
                "Africa/Dakar",
                "Africa/Nouakchott",
                "Indian/Antananarivo"
            }
        },
        new GeoData
        {
            KeyName = "sa-cl",
            DisplayName = "Chile",
            Continent = "South-America",
            Countryisocode = "CL",
            Timezones = new List<string>
            {
                "America/Argentina/Catamarca",
                "America/Argentina/Jujuy",
                "America/Argentina/La_Rioja",
                "America/Argentina/San_Juan",
                "America/Argentina/San_Luis",
                "America/Argentina/Mendoza",
                "America/Argentina/Cordoba",
                "America/Santiago",
                "America/Argentina/Buenos_Aires",
                "America/Punta_Arenas",
                "America/Argentina/Tucuman",
                "America/Argentina/Salta",
                "America/Montevideo",
                "America/Argentina/Rio_Gallegos",
                "America/Argentina/Ushuaia",
                "Argentina/Mendoza",
                "America/Buenos_Aires",
                "America/Cordoba",
                "America/Catamarca",
                "América/Catamarca"
            }
        },
        new GeoData
        {
            KeyName = "asia-jp",
            DisplayName = "Japan",
            Continent = "Asia",
            Countryisocode = "JP",
            Timezones = new List<string>
            {
                "Pacific/Guam",
                "Pacific/Saipan",
                "Asia/Tokyo"
            }
        },
        new GeoData
        {
            KeyName = "eu-italy",
            DisplayName = "Italy",
            Continent = "Europe",
            Countryisocode = "IT",
            Timezones = new List<string>
            {
                "Europe/Rome",
                "Europe/Vatican",
                "Europe/San_Marino",
                "Europe/Malta"
            }
        },
        new GeoData
        {
            KeyName = "sa-mexico",
            DisplayName = "Mexico",
            Continent = "South-America",
            Countryisocode = "MX",
            Timezones = new List<string>
            {
                "America/Hermosillo",
                "America/Guatemala",
                "America/Tijuana",
                "America/Mexico_City",
                "America/Cancun",
                "America/Monterrey",
                "America/Tegucigalpa",
                "America/Managua"
            }
        },
        new GeoData
        {
            KeyName = "eu-be",
            DisplayName = "Belgium",
            Continent = "Europe",
            Countryisocode = "BE",
            Timezones = new List<string>
            {
                "Europe/Brussels"
            }
        },
        new GeoData
        {
            KeyName = "sa-brazil",
            DisplayName = "Brazil",
            Continent = "South-America",
            Countryisocode = "BR",
            Timezones = new List<string>
            {
                "America/Santarem",
                "America/Belem",
                "America/Fortaleza",
                "America/Sao_Paulo"
            }
        }
    };

    [JsonPropertyName("name")] public string KeyName { get; set; }

    [JsonPropertyName("name-pretty")] public string DisplayName { get; set; }
    public string Continent { get; set; }

    [JsonPropertyName("country-iso-code")] public string Countryisocode { get; set; }

    [JsonPropertyName("timezones")] public List<string> Timezones { get; set; }
}