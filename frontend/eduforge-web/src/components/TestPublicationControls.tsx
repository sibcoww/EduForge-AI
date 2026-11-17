"use client";
import ContentAccessControls from "./ContentAccessControls";
export default function TestPublicationControls({testId,disabled=false}:{testId:string;disabled?:boolean}){return <ContentAccessControls id={testId} kind="test" disabled={disabled}/>;}
