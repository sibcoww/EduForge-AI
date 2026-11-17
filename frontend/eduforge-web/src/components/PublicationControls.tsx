"use client";
import ContentAccessControls from "./ContentAccessControls";
export default function PublicationControls({materialId,disabled=false}:{materialId:string;disabled?:boolean}){return <ContentAccessControls id={materialId} kind="material" disabled={disabled}/>;}
