UPDATE "SyncDocuments" SET "Payload" =
'{"images":[
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/MELROSEBLACK_1.jpg?v=1782727882&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/MELROSEBLACK_2.jpg?v=1782727884&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/MELROSEBLACK_3.jpg?v=1782727890&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/MELROSEBLACK_4.jpg?v=1782727888&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/MELROSEBLACK_5.jpg?v=1782727894&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/MELROSEBLACK_6.jpg?v=1782727892&width=1400"}}
]}'::jsonb, "LastReceivedAt" = now()
WHERE "EntityType"='model-image' AND "ExternalId"='DEMO0001-0000-4000-9000-000000000001';

UPDATE "SyncDocuments" SET "Payload" =
'{"images":[
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/LejanBLANCO_1_6874b9b9-48cd-47b1-865b-6fe8390ec6d2.jpg?v=1759848502&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/LejanBLANCO_2_a64d0423-2d7c-48b4-a846-43a1ab903bb7.jpg?v=1759848498&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/LejanBLANCO_3_817b194a-43c9-4331-abec-5adc6433d892.jpg?v=1759848498&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/LejanBLANCO_4_5bdeecb4-5dd7-43fd-8fca-c3467915805c.jpg?v=1759848498&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/LejanBLANCO_5_f4bee6b3-a47a-49df-abec-724b64bc5e37.jpg?v=1759848498&width=1400"}},
 {"image":{"uri":"https://lejanbrand.com/cdn/shop/files/LejanBLANCO_6_b808522e-5987-4d77-9bd2-9a9ee790b8e0.jpg?v=1759848498&width=1400"}}
]}'::jsonb, "LastReceivedAt" = now()
WHERE "EntityType"='model-image' AND "ExternalId"='DEMO0004-0000-4000-9000-000000000004';
